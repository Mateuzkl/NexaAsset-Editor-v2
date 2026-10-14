package
{
    import flash.desktop.NativeApplication;
    import flash.display.Sprite;
    import flash.events.InvokeEvent;
    import flash.events.ErrorEvent;
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.utils.ByteArray;
    import flash.utils.describeType;
    import mx.resources.ResourceManager;
    import by.blooddy.crypto.MD5;
    import ob.settings.ObjectBuilderSettings;
    import otlib.core.Version;
    import otlib.core.VersionStorage;
    import otlib.resources.Resources;
    import otlib.sprites.SpriteReader;
    import otlib.sprites.SpriteStorage;
    import otlib.things.ThingType;
    import otlib.things.ThingCategory;
    import otlib.things.ThingTypeStorage;
    import otlib.animation.FrameGroup;
    import otlib.utils.OTFI;
    import otlib.utils.ConvertedProjectHint;

    /** Independent AIR reader/writer oracle for the C# interoperability output. */
    [ResourceBundle("strings")]
    public class LegacyConversionTests extends Sprite
    {
        private var lines:Array = [];
        private var errors:String = "";
        public function LegacyConversionTests()
        {
            Resources.manager = ResourceManager.getInstance();
            NativeApplication.nativeApplication.addEventListener(InvokeEvent.INVOKE, run);
        }
        private function check(ok:Boolean, text:String):void { if (!ok) throw new Error(text + " " + errors); }
        private function note(text:String):void { lines.push(text); trace(text); }
        private function capture(event:ErrorEvent):void { errors += event.text + "\n"; }
        private function run(event:InvokeEvent):void
        {
            NativeApplication.nativeApplication.removeEventListener(InvokeEvent.INVOKE, run);
            var root:File = new File(event.arguments[0]); var code:int = 0;
            try
            {
                VersionStorage.getInstance().load(new File(event.arguments[1]));
                var profiles:int = 0;
                if(event.arguments.indexOf("--transparency-only")<0)for each (var folder:File in root.getDirectoryListing())
                    if (folder.isDirectory && folder.name.indexOf("legacy-") == 0 && folder.resolvePath("oracle.json").exists)
                    { testProject(folder); profiles++; }
                check(event.arguments.indexOf("--transparency-only")>=0 || profiles >= 3, "Expected at least three converted profiles.");
                var transparency:int=NativeTransparencyOracle.run(root);
                check(transparency>=16,"Expected all four categories and four transparency targets.");
                note("PASS: native transparency oracle ("+transparency+" category/profile fixtures)");
                note("PASS: independent AIR legacy conversion oracle (" + profiles + " projects)");
            }
            catch (error:Error) { code = 1; note("FAIL: " + error.getStackTrace()); }
            var stream:FileStream = new FileStream();
            try { stream.open(root.resolvePath("legacy-oracle-result.txt"), FileMode.WRITE); stream.writeUTFBytes(lines.join("\n")); }
            finally { stream.close(); }
            NativeApplication.nativeApplication.exit(code);
        }
        private function json(file:File):Object
        {
            var stream:FileStream = new FileStream();
            try { stream.open(file, FileMode.READ); check(stream.bytesAvailable <= 8388608, "Oracle size limit."); return JSON.parse(stream.readUTFBytes(stream.bytesAvailable)); }
            finally { stream.close(); }
            return null;
        }
        private function testProject(folder:File):void
        {
            errors = "";
            var expected:Object = json(folder.resolvePath("oracle.json"));
            var version:Version = new Version(); version.value = expected.version; version.valueStr = String(expected.version);
            version.datSignature = expected.datSignature; version.sprSignature = expected.sprSignature;
            var otfi:OTFI = new OTFI(); check(otfi.load(folder.resolvePath("Tibia.otfi")), "OTFI load.");
            check(otfi.extended == (expected.version>=960 || expected.sprites>65535) && otfi.transparency == expected.transparency && otfi.improvedAnimations == expected.durations && otfi.frameGroups == expected.frameGroups, "OTFI feature profile.");
            var things:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings());
            var sprites:SpriteStorage = new SpriteStorage();
            var reopened:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings());
            var reopenedSprites:SpriteStorage = new SpriteStorage();
            things.addEventListener(ErrorEvent.ERROR, capture); sprites.addEventListener(ErrorEvent.ERROR, capture);
            reopened.addEventListener(ErrorEvent.ERROR, capture); reopenedSprites.addEventListener(ErrorEvent.ERROR, capture);
            var reader:SpriteReader = new SpriteReader(otfi.features);
            try
            {
                things.load(folder.resolvePath("Tibia.dat"), version, otfi.features);
                sprites.load(folder.resolvePath("Tibia.spr"), version, otfi.features);
                check(things.loaded && sprites.loaded && errors == "", "DAT/SPR load " + folder.name);
                check(sprites.spritesCount == expected.sprites && things.signature == version.datSignature && sprites.signature == version.sprSignature, "Signatures/counts.");
                compareObjects(things, expected); comparePixels(sprites, expected);
                if(folder.resolvePath("nexa-project.json").exists){
                    var hint:Object=ConvertedProjectHint.read(folder,things,version.value,version.sprSignature);
                    check(hint && hint.id==expected.objects[0].id && hint.category==expected.objects[0].category,"Initial exported object selection hint");
                    check(!ConvertedProjectHint.read(folder,things,version.value,version.sprSignature+1),"Hint must reject a mismatched archive");
                }
                if(folder.resolvePath("all-properties-oracle.json").exists){
                    var allProperties:Object=json(folder.resolvePath("all-properties-oracle.json"));
                    var allItem:ThingType=things.getThingType(100,ThingCategory.ITEM);
                    for(var key:String in allProperties)check(allItem[key]===allProperties[key],"Mapped source property "+key+" in "+folder.name+": "+allItem[key]+" expected "+allProperties[key]);
                }
                if (folder.resolvePath("properties-oracle.json").exists)
                {
                    var props:Object = json(folder.resolvePath("properties-oracle.json"));
                    var item:ThingType = things.getThingType(100, ThingCategory.ITEM);
                    check(item.isGround && item.groundSpeed == props.groundSpeed && item.isUnpassable == props.unpassable && item.hasOffset && item.offsetX == props.offsetX && item.offsetY == props.offsetY, "Source basic flags/signed offsets.");
                    check(things.getThingType(1, ThingCategory.EFFECT).topEffect == props.topEffect, "Source topEffect/profile loss.");
                }
                reader.openSpriteFile(folder.resolvePath("Tibia.spr"));
                for (var id:uint = 1; id <= expected.sprites; id++)
                {
                    var sprite:otlib.sprites.Sprite = reader.readSprite(id); // Strict RLE check; null means an intentional empty record.
                    if (sprite) sprite.dispose();
                }
                reader.closeSpriteFile();
                var count:uint = things.itemsCount;
                check(things.addThing(things.getThingType(expected.objects[0].id, expected.objects[0].category).clone(), ThingCategory.ITEM).done, "Add converted object.");
                check(things.removeThing(things.itemsCount, ThingCategory.ITEM).done && things.itemsCount == count, "Remove converted object.");
                var sampleId:uint = expected.pixels[0].id; var original:ByteArray = sprites.getPixelsStrict(sampleId);
                var replacement:ByteArray = new ByteArray(); replacement.length = 4096;
                replacement[0] = 255; replacement[1] = 12; replacement[2] = 34; replacement[3] = 56;
                check(sprites.replaceSprite(sampleId, replacement).done && sprites.getPixelsStrict(sampleId)[1] == 12, "Replace converted sprite.");
                check(sprites.replaceSprite(sampleId, original).done, "Restore converted sprite.");
                count = sprites.spritesCount;
                check(sprites.addSprite(replacement).done, "Add converted sprite.");
                check(sprites.removeSprite(sprites.spritesCount).done && sprites.spritesCount == count, "Remove converted sprite.");
                var roundtrip:File = folder.resolvePath("roundtrip"); roundtrip.createDirectory();
                check(things.compile(roundtrip.resolvePath("Tibia.dat"), version, otfi.features), "Recompile converted DAT.");
                reopened.load(roundtrip.resolvePath("Tibia.dat"), version, otfi.features);
                check(reopened.loaded && errors == "", "Reopen converted DAT."); compareObjects(reopened, expected);
                // Check every loaded scalar property, not merely the sampled
                // geometry. The legacy writer must not discard mapped flags.
                for each (var category:String in [ThingCategory.ITEM,ThingCategory.OUTFIT,ThingCategory.EFFECT,ThingCategory.MISSILE])
                    for (id = things.getMinId(category); id <= things.getMaxId(category); id++)
                    {
                        var before:ThingType = things.getThingType(id,category), after:ThingType = reopened.getThingType(id,category);
                        for each (var field:XML in describeType(before).variable)
                            if (field.@type == "Boolean" || field.@type == "uint" || field.@type == "int" || field.@type == "String")
                                check(before[String(field.@name)] == after[String(field.@name)], "Recompiled property " + category + "/" + id + "/" + field.@name);
                    }
                // The small projects exercise the standard SPR writer without duplicating the full archive on disk.
                if (expected.sprites < 100000)
                {
                    check(sprites.compile(roundtrip.resolvePath("Tibia.spr"), version, otfi.features), "Recompile converted SPR.");
                    reopenedSprites.load(roundtrip.resolvePath("Tibia.spr"), version, otfi.features);
                    check(reopenedSprites.loaded && errors == "", "Reopen converted SPR."); comparePixels(reopenedSprites, expected);
                }
                note("PASS: " + folder.name + " / " + expected.sprites + " SPR records validated; source pixel hashes, geometry, references, animation, add/remove/replace, DAT recompile/reopen" + (expected.sprites < 100000 ? ", SPR recompile/reopen" : ""));
            }
            finally
            {
                reader.closeSpriteFile(); things.unload(); sprites.unload(); reopened.unload(); reopenedSprites.unload();
            }
        }
        private function comparePixels(storage:SpriteStorage, expected:Object):void
        {
            for each (var pixel:Object in expected.pixels)
                check(MD5.hashBytes(storage.getPixelsStrict(pixel.id)) == pixel.md5, "Source pixel hash " + pixel.id);
        }
        private function compareObjects(storage:ThingTypeStorage, expected:Object):void
        {
            for each (var obj:Object in expected.objects)
            {
                var thing:ThingType = storage.getThingType(obj.id, obj.category); check(thing != null, "Converted object ID.");
                var groups:Array = []; for each (var actual:FrameGroup in thing.frameGroups) if (actual) groups.push(actual);
                check(groups.length == obj.groups.length, "Frame group count " + obj.category + "/" + obj.id);
                for (var index:int = 0; index < groups.length; index++)
                {
                    actual = groups[index]; var group:Object = obj.groups[index];
                    check(actual.width == group.width && actual.height == group.height && actual.layers == group.layers && actual.frames == group.frames && actual.patternX == group.x && actual.patternY == group.y && actual.patternZ == group.z, "Frame geometry " + obj.id);
                    check(actual.spriteIndex.length == group.refs.length, "Reference count.");
                    for (var ref:int = 0; ref < group.refs.length; ref++) check(actual.spriteIndex[ref] == group.refs[ref], "Reference order " + obj.id + "/" + ref);
                    if (expected.durations && group.frames > 1)
                    {
                        check(actual.animationMode == group.mode && actual.loopCount == group.loops && actual.startFrame == group.start, "Animation header " + obj.id);
                        for (var frame:int = 0; frame < group.frames; frame++)
                            check(actual.frameDurations[frame].minimum == group.phases[frame].Min && actual.frameDurations[frame].maximum == group.phases[frame].Max, "Phase duration " + obj.id + "/" + frame);
                    }
                }
            }
        }
    }
}
