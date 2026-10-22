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
    import flash.utils.getTimer;
    import flash.utils.Dictionary;
    import mx.resources.ResourceManager;
    import ob.settings.ObjectBuilderSettings;
    import otlib.core.Version;
    import otlib.core.VersionStorage;
    import otlib.resources.Resources;
    import otlib.sprites.SpriteStorage;
    import otlib.things.ThingTypeStorage;
    import otlib.utils.ClientInfoLoader;
    import otlib.utils.SpriteExtent;
    import otlib.utils.SpriteRetiler;
    import otlib.utils.AssetDirectoryFinder;
    import otlib.animation.FrameGroup;
    import otlib.animation.FrameDuration;
    import otlib.things.ThingType;
    import otlib.things.ThingData;
    import otlib.things.ThingCategory;
    import otlib.sprites.SpriteData;
    import otlib.obd.OBDEncoder;
    import otlib.core.ClientFeatures;

    [ResourceBundle("strings")]
    public class WotAssetsTests extends Sprite
    {
        private var lines:Array = [];
        private var failure:String = "";
        public function WotAssetsTests()
        {
            Resources.manager = ResourceManager.getInstance();
            NativeApplication.nativeApplication.addEventListener(InvokeEvent.INVOKE, run);
        }
        private function check(value:Boolean, message:String):void { if (!value) throw new Error(message); }
        private function note(message:String):void { lines.push(message); trace(message); }
        private function failed(event:ErrorEvent):void { failure = event.text; }
        private function run(event:InvokeEvent):void
        {
            NativeApplication.nativeApplication.removeEventListener(InvokeEvent.INVOKE, run);
            var result:int = 0;
            var things:ThingTypeStorage, sprites:SpriteStorage;
            var output:File = new File(event.arguments[2]);
            var started:int = getTimer();
            try
            {
                output.createDirectory();
                var source:File = new File(event.arguments[0]);
                VersionStorage.getInstance().load(new File(event.arguments[1]).resolvePath("versions.xml"));
                var probe:ClientInfoLoader = new ClientInfoLoader();
                probe.addEventListener(ErrorEvent.ERROR, failed);
                probe.load(source.resolvePath("Tibia.dat"), source.resolvePath("Tibia.spr"), true);
                check(probe.clientInfo != null, "Wot probe failed: " + failure);
                var info:* = probe.clientInfo;
                note("Probe: " + info.clientVersionStr + ", sprites=" + info.maxSpriteId + ", dimension=" + info.spriteSize + ", alpha=" + info.transparency + ", verified=" + info.transparencyKnown);
                check(info.spriteSize == 64 && info.spriteDataSize == 16384, "Expected explicit Wot 64x64 OTFI");
                SpriteExtent.DEFAULT_VALUE = "64x64"; SpriteExtent.DEFAULT_SIZE = 64; SpriteExtent.DEFAULT_DATA_SIZE = 16384;
                var version:Version = VersionStorage.getInstance().getFromClientInfo(info);
                check(version != null, "Wot DAT/SPR profile not registered");
                check(AssetDirectoryFinder.resolve(source.parent.parent.parent.parent).nativePath == source.nativePath, "Nested Wot client discovery failed");
                testRetiling(version, output);
                things = new ThingTypeStorage(new ObjectBuilderSettings());
                things.addEventListener(ErrorEvent.ERROR, failed);
                things.load(source.resolvePath("Tibia.dat"), version, info.features);
                check(things.loaded, "Wot DAT load failed: " + failure);
                note("DAT loaded: items=" + things.itemsCount + ", outfits=" + things.outfitsCount + ", effects=" + things.effectsCount + ", missiles=" + things.missilesCount);
                sprites = new SpriteStorage(); sprites.addEventListener(ErrorEvent.ERROR, failed);
                sprites.load(source.resolvePath("Tibia.spr"), version, info.features);
                check(sprites.loaded && sprites.spritesCount == info.maxSpriteId, "Wot SPR count mismatch: " + failure);
                var samples:uint = 0;
                for (var id:uint = 1; id <= sprites.spritesCount; id += 4093)
                {
                    var pixels:ByteArray = sprites.getPixelsStrict(id);
                    check(pixels.length == 16384, "Invalid Wot pixels at " + id);
                    pixels.clear(); samples++;
                }
                pixels = sprites.getPixelsStrict(sprites.spritesCount);
                check(pixels.length == 16384, "Invalid last Wot sprite"); pixels.clear(); samples++;
                note("PASS: " + samples + " sampled Wot sprites decoded, including file regions beyond 2 GiB");
                testDat(things, version, info.features, output);
                if (event.arguments.length > 3 && event.arguments[3] == "full") testFullSprites(sprites, version, info.features, output);
                note("PASS: Wot read-only load in " + ((getTimer() - started) / 1000).toFixed(2) + "s");
            }
            catch (error:Error) { result = 1; note("FAIL: " + error.message + "\n" + error.getStackTrace()); }
            finally
            {
                if (sprites) sprites.unload(); if (things) things.unload();
                SpriteExtent.DEFAULT_VALUE = "32x32"; SpriteExtent.DEFAULT_SIZE = 32; SpriteExtent.DEFAULT_DATA_SIZE = 4096;
                var log:FileStream = new FileStream();
                try { log.open(output.resolvePath("wot-result.txt"), FileMode.WRITE); log.writeUTFBytes(lines.join("\n")); }
                finally { log.close(); }
                NativeApplication.nativeApplication.exit(result);
            }
        }

        private function extent(size:uint):void { SpriteExtent.DEFAULT_SIZE = size; SpriteExtent.DEFAULT_DATA_SIZE = size * size * 4; SpriteExtent.DEFAULT_VALUE = size + "x" + size; }
        private function same(a:ByteArray, b:ByteArray, label:String):void
        {
            check(a.length == b.length, label + " size");
            for (var i:uint = 0; i < a.length; i++) if (a[i] != b[i]) throw new Error(label + " pixel " + i);
        }
        private function synthetic(category:String):ThingData
        {
            var result:ThingData = new ThingData(); result.obdVersion = 300; result.clientVersion = 1310;
            result.thing = new ThingType(); result.thing.id = 100; result.thing.category = category;
            for (var g:uint = 0; g < (category == ThingCategory.OUTFIT ? 2 : 1); g++)
            {
                var group:FrameGroup = new FrameGroup(); group.width = 2; group.height = 1; group.exactSize = 123;
                group.layers = 2; group.patternX = 2; group.patternZ = 2; group.frames = 2; group.isAnimation = true;
                group.frameDurations = new <FrameDuration>[new FrameDuration(110, 170), new FrameDuration(120, 190)];
                group.spriteIndex = new Vector.<uint>(group.getTotalSprites(), true);
                result.thing.setFrameGroup(g, group);
                var tiles:Vector.<SpriteData> = new Vector.<SpriteData>(group.spriteIndex.length, true); result.sprites[g] = tiles;
                for (var t:uint = 0; t < tiles.length; t++)
                {
                    var pixels:ByteArray = new ByteArray();
                    for (var y:uint = 0; y < 64; y++) for (var x:uint = 0; x < 64; x++)
                        pixels.writeUnsignedInt(((x % 7 == 0 ? 0 : x % 5 == 0 ? 128 : 255) << 24) | ((g * 71 + t * 3) << 16) | (x << 8) | y);
                    var tile:SpriteData = new SpriteData(); tile.id = t + 1; tile.pixels = pixels;
                    tiles[t] = tile; group.spriteIndex[t] = tile.id;
                }
            }
            return result;
        }
        private function compare(a:ThingData, b:ThingData):void
        {
            check(a.category == b.category && a.thing.frameGroups.length == b.thing.frameGroups.length, "Category/groups changed");
            for each(var group:FrameGroup in a.thing.frameGroups)
            {
                var other:FrameGroup = b.thing.getFrameGroup(group.type);
                check(group.width == other.width && group.height == other.height && group.exactSize == other.exactSize && group.layers == other.layers && group.patternX == other.patternX && group.patternZ == other.patternZ && group.frames == other.frames, "Frame layout changed");
                check(other.frameDurations[1].minimum == group.frameDurations[1].minimum, "Frame timing changed");
                for (var i:uint = 0; i < group.spriteIndex.length; i++) same(a.sprites[group.type][i].pixels, b.sprites[group.type][i].pixels, "Retiled " + a.category + "/" + group.type + "/" + i);
            }
        }
        private function testRetiling(version:Version, output:File):void
        {
            var encoder:OBDEncoder = new OBDEncoder(new ObjectBuilderSettings());
            for each (var category:String in [ThingCategory.ITEM, ThingCategory.OUTFIT, ThingCategory.EFFECT, ThingCategory.MISSILE])
            {
                extent(64); var data:ThingData = synthetic(category);
                var split:ThingData = SpriteRetiler.data(data, 64, 32);
                check(split.getFrameGroup(0).width == 4 && split.getFrameGroup(0).height == 2, "64->32 did not split dimensions");
                var joined:ThingData = SpriteRetiler.data(split, 32, 64); compare(data, joined);
                var encoded:ByteArray = encoder.encode(data), again:ByteArray = encoder.encode(data);
                same(encoded, again, "Repeat OBD export");
                var copy:ByteArray = new ByteArray(); copy.writeBytes(encoded);
                extent(32); var decoded32:ThingData = encoder.decode(encoded);
                check(decoded32.getFrameGroup(0).width == 4, "OBD was not encoded in standard 32x32");
                extent(64); var decoded64:ThingData = encoder.decode(copy); compare(data, decoded64);
                check(data.getFrameGroup(0).width == 2, "Export mutated source metadata");
                SpriteRetiler.dispose(decoded32); SpriteRetiler.dispose(decoded64); SpriteRetiler.dispose(split); SpriteRetiler.dispose(joined); SpriteRetiler.dispose(data);
                encoded.clear(); again.clear(); copy.clear();
            }
            var pixels:ByteArray = new ByteArray(); pixels.length = 16384; pixels[0] = 255; pixels[1] = 79; pixels[2] = 23; pixels[3] = 10;
            var features:ClientFeatures = new ClientFeatures(true, true, true, true);
            var storage:SpriteStorage = new SpriteStorage(); storage.addEventListener(ErrorEvent.ERROR, failed); storage.createNew(version, features);
            check(storage.replaceSprite(1, pixels).done, "Synthetic 64x64 replace failed");
            for each(var name:String in ["tiles32.spr", "multi/spr_index.dat"])
            {
                var file:File = output.resolvePath(name); file.parent.createDirectory();
                check(storage.compileToStaging(file, version, features, 32), "Retiled SPR compile failed: " + failure);
                extent(32); var reopened:SpriteStorage = new SpriteStorage(); reopened.addEventListener(ErrorEvent.ERROR, failed); reopened.load(file, version, features);
                check(reopened.loaded && reopened.spritesCount == 4, "Retiled archive count failed: " + failure);
                for(var part:uint = 0; part < 4; part++) same(SpriteRetiler.split64(pixels, part), reopened.getPixelsStrict(part + 1), "Archive tile order");
                reopened.unload(); extent(64);
            }
            check(storage.changed && storage.spritesCount == 1, "Export copy marked source saved or changed source count");
            storage.unload(); pixels.clear();
            note("PASS: item/outfit/effect/missile lossless 64<->32, alpha, patterns, layers, phases, both outfit groups, repeat OBD v3, standard and multi-SPR");
        }
        private function testDat(things:ThingTypeStorage, version:Version, features:ClientFeatures, output:File):void
        {
            for each(var size:uint in [64, 32])
            {
                extent(64); var file:File = output.resolvePath("wot-" + size + ".dat");
                check(things.compileToStaging(file, version, features, size), "Wot DAT compile failed: " + failure);
                extent(size); var reopened:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings()); reopened.addEventListener(ErrorEvent.ERROR, failed);
                reopened.load(file, version, features); check(reopened.loaded, "Wot DAT reopen failed: " + failure);
                check(reopened.itemsCount == 59479 && reopened.outfitsCount == 2431, "Wot counts changed");
                for(var id:uint = 2417; id <= 2425; id++) check(reopened.getOutfitType(id).wotFlag28, "Wot flag 0x28 lost");
                for each(var category:String in [ThingCategory.ITEM, ThingCategory.OUTFIT, ThingCategory.EFFECT, ThingCategory.MISSILE])
                    for(id = things.getMinId(category); id <= things.getMaxId(category); id++)
                    {
                        var before:ThingType = things.getThingType(id, category), after:ThingType = reopened.getThingType(id, category);
                        var expected:ThingType = size == 64 ? before : SpriteRetiler.archiveThing(before);
                        for each(var group:FrameGroup in expected.frameGroups)
                        {
                            var actual:FrameGroup = after.getFrameGroup(group.type);
                            check(actual && actual.width == group.width && actual.height == group.height && actual.spriteIndex.length == group.spriteIndex.length, "DAT layout changed");
                            for (var n:uint = 0; n < actual.spriteIndex.length; n++) check(actual.spriteIndex[n] == group.spriteIndex[n], "DAT sprite reference changed");
                        }
                    }
                reopened.unload(); note("PASS: complete Wot DAT " + size + "x" + size + " compile/reopen, all references and nine custom flags");
            }
            extent(64);
        }
        private function testFullSprites(sprites:SpriteStorage, version:Version, features:ClientFeatures, output:File):void
        {
            for each(var size:uint in [64, 32])
            {
                extent(64); var file:File = output.resolvePath("full-" + size + ".spr"); var start:int = getTimer();
                note("START: full Wot SPR " + size + "x" + size + " compile");
                check(sprites.compileToStaging(file, version, features, size), "Full SPR compile failed: " + failure);
                extent(size); var reopened:SpriteStorage = new SpriteStorage(); reopened.addEventListener(ErrorEvent.ERROR, failed); reopened.load(file, version, features);
                check(reopened.loaded && reopened.spritesCount == sprites.spritesCount * (size == 32 ? 4 : 1), "Full SPR count mismatch");
                for(var id:uint = 1; id <= sprites.spritesCount; id += 4093)
                {
                    extent(64); var original:ByteArray = sprites.getPixelsStrict(id);
                    extent(size);
                    if (size == 64) same(original, reopened.getPixelsStrict(id), "Full 64x64 pixels");
                    else for(var part:uint = 0; part < 4; part++) same(SpriteRetiler.split64(original, part), reopened.getPixelsStrict((id - 1) * 4 + part + 1), "Full 32x32 pixels");
                    original.clear();
                }
                reopened.unload(); note("PASS: full Wot SPR " + size + "x" + size + ", bytes=" + file.size + ", seconds=" + ((getTimer() - start) / 1000).toFixed(2));
            }
            extent(64);
        }
    }
}
