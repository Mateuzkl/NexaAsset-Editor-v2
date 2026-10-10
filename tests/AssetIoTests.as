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
    import flash.utils.Endian;
    import flash.utils.getTimer;
    import mx.resources.ResourceManager;
    import ob.settings.ObjectBuilderSettings;
    import otlib.core.ClientFeatures;
    import otlib.core.Version;
    import otlib.core.VersionStorage;
    import otlib.events.ProgressEvent;
    import otlib.resources.Resources;
    import otlib.sprites.SpriteReader;
    import otlib.sprites.SpriteStorage;
    import otlib.things.ThingTypeStorage;
    import otlib.utils.ClientInfoLoader;
    import otlib.utils.OTFI;

    [ResourceBundle("strings")]
    public class AssetIoTests extends Sprite
    {
        private var testRoot:File;
        private var lines:Array = [];
        private var errorText:String = "";
        private var started:int;
        private var progressCount:uint;
        private var lastProgress:uint;

        public function AssetIoTests()
        {
            Resources.manager = ResourceManager.getInstance();
            NativeApplication.nativeApplication.addEventListener(InvokeEvent.INVOKE, run);
        }

        private function run(event:InvokeEvent):void
        {
            NativeApplication.nativeApplication.removeEventListener(InvokeEvent.INVOKE, run);
            var exitCode:int = 0;
            testRoot = new File(event.arguments[0]);
            started = getTimer();
            try
            {
                testRoot.createDirectory();
                testBitmaps();
                testFormats();
                if (event.arguments.length > 1) testReal(new File(event.arguments[1]), new File(event.arguments[2]));
                note("PASS: all tests (" + ((getTimer() - started) / 1000).toFixed(1) + "s)");
            }
            catch (error:Error) { exitCode = 1; note("FAIL: " + error.message + "\n" + error.getStackTrace()); }
            finally
            {
                var stream:FileStream = new FileStream();
                try { stream.open(testRoot.resolvePath("result.txt"), FileMode.WRITE); stream.writeUTFBytes(lines.join("\n")); }
                finally { stream.close(); }
                NativeApplication.nativeApplication.exit(exitCode);
            }
        }

        private function check(ok:Boolean, message:String):void { if (!ok) throw new Error(message); }
        private function note(text:String):void { lines.push(text); trace(text); }
        private function capture(event:ErrorEvent):void { errorText = event.text; }
        private function progress(event:ProgressEvent):void
        {
            check(event.loaded >= lastProgress, "SPR progress must be monotonic.");
            lastProgress = event.loaded;
            progressCount++;
        }

        private function testBitmaps():void
        {
            var pixels:ByteArray = new ByteArray(); pixels.length = 4096;
            pixels[0] = 255; pixels[1] = 10; pixels[2] = 20; pixels[3] = 30;
            var sprite:otlib.sprites.Sprite = new otlib.sprites.Sprite(1, false);
            sprite.setPixels(pixels);
            check(sprite.getBitmap().getPixel(0, 0) == 0x0A141E, "Initial bitmap.");
            var clone:otlib.sprites.Sprite = sprite.clone();
            clone.dispose();
            check(sprite.getBitmap().getPixel(0, 0) == 0x0A141E, "Clone disposal must not destroy source bitmap.");
            pixels[1] = 90;
            sprite.setPixels(pixels);
            check(sprite.getBitmap().getPixel(0, 0) == 0x5A141E, "Bitmap cache invalidation.");
            sprite.setBitmap(sprite.getBitmap());
            check(sprite.getBitmap().getPixel(0, 0) == 0x5A141E, "Self bitmap assignment.");
            sprite.dispose();
            var a:ClientFeatures = new ClientFeatures();
            var b:ClientFeatures = a.clone(); b.frameGroups = true;
            check(a.differs(b), "Frame-group differences must trigger reload.");
            note("PASS: bitmap ownership/cache and feature comparison");
        }

        private function version():Version
        {
            var v:Version = new Version(); v.value = 860; v.valueStr = "8.60 test";
            v.datSignature = 0x55667788; v.sprSignature = 0x11223344;
            return v;
        }

        private function fixture(file:File, count:uint):void
        {
            var stream:FileStream = new FileStream();
            try
            {
                stream.open(file, FileMode.WRITE); stream.endian = Endian.LITTLE_ENDIAN;
                stream.writeUnsignedInt(version().sprSignature); stream.writeUnsignedInt(count);
                for (var id:uint = 1; id <= count; id++)
                    stream.writeUnsignedInt(id >= count - 1 ? 8 + count * 4 + (id - count + 1) * 12 : 0);
                for (id = count - 1; id <= count; id++)
                {
                    stream.writeByte(255); stream.writeByte(0); stream.writeByte(255); stream.writeShort(7);
                    stream.writeShort(0); stream.writeShort(1);
                    stream.writeByte(id == count ? 90 : 10); stream.writeByte(20); stream.writeByte(30);
                }
            }
            finally { stream.close(); }
        }

        private function testFormats():void
        {
            var source:File = testRoot.resolvePath("source.spr"); fixture(source, 65536);
            var storage:SpriteStorage = new SpriteStorage();
            storage.addEventListener(ErrorEvent.ERROR, capture);
            storage.addEventListener(ProgressEvent.PROGRESS, progress);
            var f:ClientFeatures = new ClientFeatures(true);
            storage.load(source, version(), f);
            var original:ByteArray = storage.getPixels(65536);
            storage.replaceSprite(65536, original); // Exercise cached/edited sprites too.
            var rejected:File = testRoot.resolvePath("must-not-truncate.spr");
            check(!storage.compile(rejected, version(), new ClientFeatures(false)), "Reject 65536 in 16-bit mode.");
            check(!rejected.exists, "Capacity failure must not create destination.");
            var unified:File = testRoot.resolvePath("extended.spr");
            check(storage.compile(unified, version(), f), "32-bit compile: " + errorText);
            check(progressCount >= 2 && lastProgress == 65536, "Actual SPR progress events.");
            var alpha:ClientFeatures = new ClientFeatures(true, true);
            lastProgress = 0;
            check(storage.compile(testRoot.resolvePath("alpha.spr"), version(), alpha), "Transparency conversion.");
            check(!storage.getSprite(65536).transparent, "Export must not mutate cached source transparency.");
            var multiRoot:File = testRoot.resolvePath("multi"); multiRoot.createDirectory(); lastProgress = 0;
            check(storage.compile(multiRoot.resolvePath("spr_index.dat"), version(), f), "Multi-SPR compile.");
            check(multiRoot.resolvePath("FileParts/part_2.spr").exists, "Multi-SPR boundary split.");
            storage.unload();
            check(!storage.loaded && storage.spritesCount == 0, "Unload resets storage.");

            var reader:SpriteReader = new SpriteReader(f);
            reader.openSpriteFile(multiRoot.resolvePath("spr_index.dat"));
            check(reader.readSpriteCount() == 65536, "Multi-SPR logical count.");
            check(reader.readSprite(65535).getPixels()[1] == 10 && reader.readSprite(65536).getPixels()[1] == 90, "Part boundary pixels.");
            reader.closeSpriteFile();
            var part:File = multiRoot.resolvePath("FileParts/part_2.spr");
            var renamed:File = multiRoot.resolvePath("FileParts/part_2.closed");
            part.moveTo(renamed); renamed.moveTo(part); // Closed stream must release Windows file handle.
            for (var i:uint = 0; i < 25; i++)
            {
                reader.openSpriteFile(multiRoot.resolvePath("spr_index.dat"));
                reader.readSprite(65536); reader.closeSpriteFile();
            }
            storage.load(multiRoot.resolvePath("spr_index.dat"), version(), f); lastProgress = 0;
            check(storage.compile(testRoot.resolvePath("merged.spr"), version(), f), "Multi-SPR -> standard SPR.");
            storage.unload();
            reader.openSpriteFile(testRoot.resolvePath("merged.spr"));
            check(reader.readSpriteCount() == 65536 && reader.readSprite(65536).getPixels()[1] == 90, "Merged round-trip.");
            reader.closeSpriteFile();

            // Non-sequential index: IDs must follow locators, not concatenation order.
            var edit:FileStream = new FileStream();
            edit.open(multiRoot.resolvePath("spr_index.dat"), FileMode.UPDATE); edit.endian = Endian.LITTLE_ENDIAN;
            edit.position = 12; edit.writeUnsignedInt((2 << 16) | 1); edit.close();
            reader.openSpriteFile(multiRoot.resolvePath("spr_index.dat"));
            check(reader.readSprite(1).getPixels()[1] == 90, "Honor non-sequential logical locator.");
            reader.closeSpriteFile();
            edit.open(multiRoot.resolvePath("spr_index.dat"), FileMode.UPDATE);
            edit.position = 12; edit.writeUnsignedInt(0); edit.close();
            var bad:Boolean = false;
            try { reader.openSpriteFile(multiRoot.resolvePath("spr_index.dat")); } catch (error:Error) { bad = true; }
            finally { reader.closeSpriteFile(); }
            check(bad, "Reject invalid multi-SPR locator.");

            var legacySource:File = testRoot.resolvePath("legacy-source.spr"); fixture(legacySource, 65535);
            storage.load(legacySource, version(), f); lastProgress = 0;
            check(storage.compile(testRoot.resolvePath("legacy.spr"), version(), new ClientFeatures(false)), "65,535 must fit exactly.");
            storage.unload();
            reader = new SpriteReader(new ClientFeatures(false)); reader.openSpriteFile(testRoot.resolvePath("legacy.spr"));
            check(reader.readSpriteCount() == 65535 && reader.readSprite(65535).getPixels()[1] == 90, "No off-by-one/truncation at 65,535.");
            reader.closeSpriteFile();
            note("PASS: 16/32-bit boundaries, transparency, monotonic progress, multi-SPR, merge, 25 close cycles, invalid locator");
        }

        private function testReal(source:File, config:File):void
        {
            VersionStorage.getInstance().load(config.resolvePath("versions.xml"));
            var probe:ClientInfoLoader = new ClientInfoLoader();
            probe.addEventListener(ErrorEvent.ERROR, capture);
            probe.load(source.resolvePath("Tibia.dat"), source.resolvePath("spr_index.dat"), true);
            check(probe.clientInfo != null, "Real file probe: " + errorText);
            var v:Version = VersionStorage.getInstance().getFromClientInfo(probe.clientInfo);
            var f:ClientFeatures = probe.clientInfo.features;
            var things:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings());
            things.addEventListener(ErrorEvent.ERROR, capture);
            things.load(source.resolvePath("Tibia.dat"), v, f);
            check(things.loaded, "Real DAT load: " + errorText);
            note("PASS: real DAT load (items " + things.itemsCount + ", outfits " + things.outfitsCount + ")");
            var out:File = testRoot.resolvePath("real-standard"); out.createDirectory();
            check(things.compile(out.resolvePath("Tibia.dat"), v, f), "Real DAT compile: " + errorText);
            var sprites:SpriteStorage = new SpriteStorage(); sprites.addEventListener(ErrorEvent.ERROR, capture);
            sprites.load(source.resolvePath("spr_index.dat"), v, f);
            note("Real SPR loaded: " + sprites.spritesCount + " sprites; compiling standard SPR...");
            check(sprites.compile(out.resolvePath("Tibia.spr"), v, f), "Real SPR merge: " + errorText);
            new OTFI(f, "Tibia.dat", "Tibia.spr", 32, 4096).save(out.resolvePath("Tibia.otfi"));
            var reopened:SpriteStorage = new SpriteStorage(); reopened.load(out.resolvePath("Tibia.spr"), v, f);
            check(reopened.spritesCount == sprites.spritesCount, "Real logical sprite count preserved.");
            // Across every physical part boundary plus representative random logical IDs.
            for (var id:uint = 1; id <= sprites.spritesCount; id += 4093)
                check(samePixels(sprites.getPixels(id), reopened.getPixels(id)), "Real sprite pixels differ at " + id);
            check(samePixels(sprites.getPixels(sprites.spritesCount), reopened.getPixels(sprites.spritesCount)), "Real last sprite.");
            reopened.unload(); sprites.unload(); things.unload();
            var datReload:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings());
            datReload.addEventListener(ErrorEvent.ERROR, capture); datReload.load(out.resolvePath("Tibia.dat"), v, f);
            check(datReload.loaded, "Recompiled DAT reopen: " + errorText); datReload.unload();
            note("PASS: real multi-SPR -> standard DAT/SPR compile, sampled pixels, reopen and close");
        }

        private function samePixels(a:ByteArray, b:ByteArray):Boolean
        {
            if (!a || !b || a.length != b.length) return false;
            for (var i:uint = 0; i < a.length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
