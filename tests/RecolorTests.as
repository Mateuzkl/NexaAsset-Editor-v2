package
{
    import flash.display.BitmapData;
    import flash.filesystem.File;
    import flash.utils.ByteArray;
    import ob.recolor.*;
    import ob.settings.ObjectBuilderSettings;
    import otlib.animation.*;
    import otlib.core.*;
    import otlib.sprites.*;
    import otlib.things.*;
    import otlib.utils.OTFI;
    import otlib.storages.events.StorageEvent;
    use namespace otlib_internal;

    public final class RecolorTests
    {
        public static function run(root:File):void
        {
            masksAndColors();
            var version:Version = VersionStorage.getInstance().getByValueString("10.98");
            var features:ClientFeatures = new ClientFeatures(true, true, true, true);
            var things:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings());
            var sprites:FailingRecolorSprites = new FailingRecolorSprites();
            things.createNew(version, features);
            sprites.createNew(version, features);
            var mutations:int;
            sprites.addEventListener(StorageEvent.MUTATION, function(e:StorageEvent):void { mutations++; });
            sprites.invalidate(); sprites.invalidate();
            check(mutations == 2, "Dirty storage stopped reporting mutations.");
            var red:ByteArray = tile(255, 255, 0, 0);
            var spriteId:uint = sprites.addSprite(red).list[0].id;
            var green:ByteArray = tile(255, 0, 255, 0);
            var templateId:uint = sprites.addSprite(green).list[0].id;
            var settings:Object = options();
            var ids:Object = {};

            for each (var category:String in ["item", "outfit", "effect", "missile"])
            {
                var thing:ThingType = ThingType.create(0, category, true, 120);
                thing.name = "Recolor fixture";
                for each (var group:FrameGroup in thing.frameGroups)
                {
                    if (!group) continue;
                    group.width = group.height = 2;
                    group.frames = 2;
                    group.isAnimation = true;
                    group.layers = category == "outfit" ? 2 : 1;
                    group.frameDurations = new <FrameDuration>[new FrameDuration(120, 120), new FrameDuration(150, 150)];
                    group.spriteIndex = new Vector.<uint>(group.getTotalSprites());
                    for (var index:int = 0; index < group.spriteIndex.length; index++)
                        group.spriteIndex[index] = category == "outfit" && int(index / 4) % 2 == 1 ? templateId : spriteId;
                }
                var sourceId:uint = things.addThing(thing, category).list[0].id;
                var other:ThingType = thing.clone();
                var otherId:uint = things.addThing(other, category).list[0].id;
                var mask:RecolorMask = new RecolorMask(64, 64);
                mask.rectangle(0, 0, 15, 15);
                var selections:Object = {"0:0":mask.pixels};
                var originalGroup:FrameGroup = thing.getFrameGroup(0);
                var count:uint = sprites.spritesCount;
                check(RecolorSave.apply(things, sprites, features, sourceId, category, settings, {}, false) == 0,
                    "Empty selection must not mutate the object.");
                check(sprites.spritesCount == count, "Empty mask added SPR records.");

                check(RecolorSave.apply(things, sprites, features, sourceId, category, settings, selections, false) == sourceId,
                    "Replace current changed the object ID.");
                var edited:ThingType = things.getThingType(sourceId, category);
                var changedGroup:FrameGroup = edited.getFrameGroup(0);
                check(changedGroup.spriteIndex[3] != spriteId && changedGroup.spriteIndex[0] == spriteId,
                    "Mask must map to the top-left tile, leaving other tiles intact.");
                check(changedGroup.spriteIndex[4] == (category == "outfit" ? templateId : spriteId),
                    "Unselected layer/direction changed.");
                check(changedGroup.frames == 2 && changedGroup.frameDurations[1].minimum == 150,
                    "Recolor changed animation timing.");
                var pixels:ByteArray = sprites.getPixelsStrict(changedGroup.spriteIndex[3]);
                check(pixels[1] == 0 && pixels[2] == 255 && pixels[3] == 0, "Hue rotation did not make red green.");
                var outside:int = (20 * 32 + 20) * 4;
                check(pixels[outside + 1] == 255 && pixels[outside + 2] == 0, "Unselected pixel changed.");
                pixels.clear();
                check(things.getThingType(otherId, category).getFrameGroup(0).spriteIndex[3] == spriteId,
                    "Shared source references changed in another object.");
                pixels = sprites.getPixelsStrict(spriteId);
                check(pixels[1] == 255 && pixels[2] == 0, "Original shared sprite was overwritten.");
                pixels.clear();

                var newId:uint = RecolorSave.apply(things, sprites, features, sourceId, category, settings, null, true);
                check(newId > otherId, "Create New must allocate a new object.");
                var clone:ThingType = things.getThingType(newId, category);
                for each (group in clone.frameGroups)
                {
                    if (!group) continue;
                    for (index = 0; index < group.spriteIndex.length; index++)
                        check(group.spriteIndex[index] > count, "Create New reused a source sprite ID.");
                }
                if (category == "outfit")
                {
                    pixels = sprites.getPixelsStrict(clone.getFrameGroup(0).spriteIndex[4]);
                    check(pixels[2] == 255 && pixels[1] == 0, "Outfit template layer must retain its dye data.");
                    pixels.clear();
                }
                ids[category] = newId;
            }
            var before:uint = sprites.spritesCount;
            var unchanged:ThingType = things.getThingType(101, "item");
            var maxBefore:uint = things.getMaxId("item");
            var failureMask:RecolorMask = new RecolorMask(64, 64);
            failureMask.fill(true);
            sprites.successfulAdds = 1;
            try
            {
                RecolorSave.apply(things, sprites, features, 101, "item", settings, {"0:0":failureMask.pixels}, true);
                throw new Error("Injected allocation failure was ignored.");
            }
            catch (error:Error) { check(error.message != "Injected allocation failure was ignored.", error.message); }
            sprites.successfulAdds = int.MAX_VALUE;
            check(sprites.spritesCount == before && things.getMaxId("item") == maxBefore &&
                things.getThingType(101, "item") === unchanged, "Partial recolor save did not roll back.");
            try
            {
                RecolorSave.apply(things, sprites, features, 101, "item", settings, {"0:0":new ByteArray()}, false);
                throw new Error("Malformed mask accepted.");
            }
            catch (maskError:Error) { check(maskError.message != "Malformed mask accepted.", maskError.message); }
            check(sprites.spritesCount == before, "Invalid mask left partial SPR records.");
            // Equivalent masks in repeated animation slots need only one new SPR ID.
            var repeated:ThingType = ThingType.create(0, "effect", false, 100);
            var repeatedGroup:FrameGroup = repeated.getFrameGroup(0);
            repeatedGroup.frames = 2;
            repeatedGroup.isAnimation = true;
            repeatedGroup.spriteIndex = new <uint>[spriteId, spriteId];
            repeatedGroup.frameDurations = new <FrameDuration>[new FrameDuration(100,100), new FrameDuration(100,100)];
            var repeatedId:uint = things.addThing(repeated, "effect").list[0].id;
            var repeatedMask:RecolorMask = new RecolorMask(32,32); repeatedMask.fill(true);
            var originalCount:uint = sprites._spritesCount;
            sprites._spritesCount = 65534;
            var limited:ClientFeatures = features.clone(); limited.extended = false;
            try
            {
                RecolorSave.apply(things,sprites,limited,repeatedId,"effect",settings,
                    {"0:0":repeatedMask.pixels,"0:1":repeatedMask.pixels},false);
                check(sprites.spritesCount == 65535, "Last SPR ID or masked frame reuse failed.");
                check(things.getThingType(repeatedId,"effect").getFrameGroup(0).spriteIndex[1] == 65535,
                    "Repeated masked frame allocated another record.");
            }
            finally
            {
                if (sprites._sprites[65535]) sprites._sprites[65535].dispose();
                delete sprites._sprites[65535]; sprites._spritesCount = originalCount;
                things.removeThing(repeatedId,"effect");
            }

            var client:File = root.resolvePath("client");
            client.createDirectory();
            check(things.compile(client.resolvePath("Tibia.dat"), version, features), "DAT compile failed.");
            check(sprites.compile(client.resolvePath("Tibia.spr"), version, features), "SPR compile failed.");
            new OTFI(features, "Tibia.dat", "Tibia.spr", 32, 4096).save(client.resolvePath("Tibia.otfi"));
            things.unload();
            sprites.unload();
            things.load(client.resolvePath("Tibia.dat"), version, features);
            sprites.load(client.resolvePath("Tibia.spr"), version, features);
            check(things.loaded && sprites.loaded, "Recompiled recolor assets could not reopen.");
            for (category in ids)
                check(things.hasThingType(category, ids[category]), "Missing recolored category after reopen.");
            things.unload();
            sprites.unload();
            red.clear();
            green.clear();
            trace("PASS: Recolor masks, HSL, alpha, all categories, tile order, frame/layer protection, shared sprites, new IDs, compile/reopen");
        }

        public static function options():Object
        {
            return {hsl:true, hue:120, saturation:0, lightness:0, colorize:false,
                replace:false, source:0xFF0000, target:0x0000FF, tolerance:0, shading:false};
        }

        private static function masksAndColors():void
        {
            var image:BitmapData = new BitmapData(8, 8, true, 0);
            image.setPixel32(0, 0, 0xFFFF0000);
            image.setPixel32(1, 0, 0xFFFF1000);
            image.setPixel32(7, 7, 0xFFFF0000);
            var mask:RecolorMask = new RecolorMask(8, 8);
            mask.wand(image, 0, 0, 20, false);
            check(mask.pixels[0] == 1 && mask.pixels[1] == 1 && mask.pixels[63] == 0, "Wand crossed disconnected regions.");
            mask.wand(image, 7, 7, 0, true);
            check(mask.pixels[0] == 1 && mask.pixels[63] == 1, "Shift + wand did not add selection.");
            image.setPixel32(0,0,0);
            mask.wand(image,0,0,0,true);
            check(mask.pixels[63] == 1,"Shift transparent wand erased the mask.");
            mask.wand(image,0,0,0,false);
            check(mask.pixels[63] == 0,"Transparent wand retained an old selection.");
            mask.fill(false);
            mask.stroke(0, 0, 7, 7, 1, true);
            for (var i:int = 0; i < 8; i++) check(mask.pixels[i * 9] == 1, "Brush stroke skipped a pixel.");
            mask.stroke(0, 0, 0, 0, 1, false);
            check(mask.pixels[0] == 0, "Eraser did not unselect.");
            mask.invert();
            check(mask.pixels[0] == 1 && mask.pixels[9] == 0, "Selection invert failed.");
            image.dispose();

            var group:FrameGroup = new FrameGroup();
            var raw:ByteArray = tile(255, 255, 0, 0);
            raw[0] = 128;
            raw[4] = 0;
            raw[5] = 47;
            var options:Object = options();
            var result:ByteArray = SpriteRecolor.applyTile(raw, options, null, group, 0);
            check(result[0] == 128 && result[1] == 0 && result[2] == 255 && result[3] == 0, "HSL hue/alpha failed.");
            check(result[4] == 0 && result[5] == 47, "Hidden RGB in transparent pixels changed.");
            result.clear();
            options.hsl = false; options.replace = true;
            result = SpriteRecolor.applyTile(raw, options, null, group, 0);
            check(result[1] == 0 && result[2] == 0 && result[3] == 255, "Selective RGB replacement failed.");
            result.clear();
            options.replace = false;
            check(SpriteRecolor.applyTile(raw, options, null, group, 0) == null, "Disabled effects should return unchanged.");
            raw.clear();
        }

        private static function tile(alpha:int, red:int, green:int, blue:int):ByteArray
        {
            var bytes:ByteArray = new ByteArray();
            for (var i:int = 0; i < 1024; i++)
            {
                bytes.writeByte(alpha); bytes.writeByte(red); bytes.writeByte(green); bytes.writeByte(blue);
            }
            bytes.position = 0;
            return bytes;
        }

        private static function check(value:Boolean, message:String):void
        {
            if (!value) throw new Error(message);
        }
    }
}
