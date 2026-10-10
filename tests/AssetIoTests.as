package
{
    import flash.desktop.NativeApplication;
    import flash.display.Sprite;
    import flash.events.InvokeEvent;
    import flash.events.ErrorEvent;
    import flash.events.Event;
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
    import otlib.utils.AssetOutputTransaction;
    import otlib.utils.AssetFeatureValidator;
    import otlib.utils.ClientInfo;
    import otlib.storages.events.StorageEvent;
    import otlib.core.MetadataControllerStorage;
    import otlib.things.MetadataWriter;
    import otlib.things.ThingType;
    import otlib.things.ThingCategory;

    [ResourceBundle("strings")]
    public class AssetIoTests extends Sprite
    {
        private var testRoot:File;
        private var lines:Array = [];
        private var errorText:String = "";
        private var started:int;
        private var progressCount:uint;
        private var lastProgress:uint;
        private var progressError:String = "";
        private var compileEvents:uint;
        private var changeEvents:uint;

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
                testFeatureWarnings();
                testTransaction();
                testExtendedItems();
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
            if (event.label != "Saving SPR") return;
            // Event-listener exceptions are swallowed by AIR; assert outside dispatch.
            if (event.loaded < lastProgress) progressError = "SPR progress must be monotonic.";
            lastProgress = event.loaded;
            progressCount++;
        }
        private function compiled(event:Event):void { compileEvents++; }
        private function changed(event:Event):void { changeEvents++; }

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
            var sparse:SpriteStorage = new SpriteStorage(); sparse.createNew(version(), f);
            check(sparse.compile(testRoot.resolvePath("all-empty.spr"), version(), f), "All-empty sprite table.");
            sparse.unload();
            var sparseReader:SpriteReader = new SpriteReader(f); sparseReader.openSpriteFile(testRoot.resolvePath("all-empty.spr"));
            check(sparseReader.readSpriteCount() == 1 && sparseReader.readSprite(1) == null, "All-empty table reopens.");
            sparseReader.closeSpriteFile();
            var unified:File = testRoot.resolvePath("extended.spr");
            check(storage.compile(unified, version(), f), "32-bit compile: " + errorText);
            check(progressCount >= 2 && lastProgress == 65536, "Actual SPR progress events.");
            check(!progressError, progressError);
            var alpha:ClientFeatures = new ClientFeatures(true, true);
            lastProgress = 0;
            check(storage.compile(testRoot.resolvePath("alpha.spr"), version(), alpha), "Transparency conversion.");
            check(!storage.getSprite(65536).transparent, "Export must not mutate cached source transparency.");
            var alphaReader:SpriteReader = new SpriteReader(alpha); alphaReader.openSpriteFile(testRoot.resolvePath("alpha.spr"));
            check(samePixels(original, alphaReader.readSprite(65536).getPixels()), "Direct RLE alpha conversion preserves opaque pixels.");
            alphaReader.closeSpriteFile();
            var multiRoot:File = testRoot.resolvePath("multi"); multiRoot.createDirectory(); lastProgress = 0;
            storage.addEventListener(StorageEvent.COMPILE, compiled);
            storage.addEventListener(StorageEvent.CHANGE, changed);
            check(storage.compile(multiRoot.resolvePath("spr_index.dat"), version(), f), "Multi-SPR compile.");
            check(compileEvents == 1 && changeEvents == 1, "Multi-SPR must emit both completion events exactly once.");
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
            storage.replaceSprite(65536, original);
            check(storage.compile(multiRoot.resolvePath("spr_index.dat"), version(), f), "Recompile multi-SPR in place: " + errorText);
            check(!storage.changed && storage.getPixels(65536)[1] == 90, "In-place save rebinds storage without lost pixels.");
            var backupFound:Boolean = false;
            for each (var backup:File in multiRoot.getDirectoryListing())
                if (backup.isDirectory && backup.name.indexOf(".ob-backup-") == 0)
                    backupFound = backup.resolvePath("spr_index.dat").exists && backup.resolvePath("FileParts/part_2.spr").exists;
            check(backupFound, "Previous complete multi-SPR generation is recoverable.");
            lastProgress = 0;
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
            var legacyInput:SpriteStorage = new SpriteStorage();
            legacyInput.load(testRoot.resolvePath("legacy.spr"), version(), new ClientFeatures(false));
            var legacyMulti:File = testRoot.resolvePath("legacy-to-multi/spr_index.dat"); legacyMulti.parent.createDirectory();
            check(legacyInput.compile(legacyMulti, version(), f), "Legacy SPR -> multi-SPR."); legacyInput.unload();
            check(!progressError, progressError);
            note("PASS: 16/32-bit boundaries, transparency, monotonic progress, multi-SPR, merge, 25 close cycles, invalid locator");
        }

        private function testTransaction():void
        {
            var root:File = testRoot.resolvePath("transaction"); root.createDirectory();
            writeText(root.resolvePath("a.dat"), "old a"); writeText(root.resolvePath("b.spr"), "old b");
            var stage:File = AssetOutputTransaction.createStage(root);
            writeText(stage.resolvePath("a.dat"), "new a"); writeText(stage.resolvePath("b.spr"), "new b");
            var lock:FileStream = new FileStream(); lock.open(root.resolvePath("b.spr"), FileMode.READ);
            var failed:Boolean = false;
            try { AssetOutputTransaction.publish(stage, root, ["a.dat", "b.spr"]); }
            catch (error:Error) { failed = true; note("Expected publication failure: " + error.message); }
            finally { lock.close(); }
            check(failed && readText(root.resolvePath("a.dat")) == "old a" && readText(root.resolvePath("b.spr")) == "old b",
                "Locked destination rolls back already moved files.");
            var backup:File = AssetOutputTransaction.publish(stage, root, ["a.dat", "b.spr"]);
            check(readText(root.resolvePath("a.dat")) == "new a" && readText(backup.resolvePath("b.spr")) == "old b", "Publish and retained backup.");
            note("PASS: multi-file publication rollback and retained backups");
        }

        private function testFeatureWarnings():void
        {
            var file:File = testRoot.resolvePath("legacy.spr");
            var header:Object = SpriteReader.probeHeader(file, true);
            check(header.known && !header.extended, "Detect a legacy SPR even with the wrong initial hint.");
            file = testRoot.resolvePath("feature-rgb.spr"); featureFixture(file, false, false);
            var reader:SpriteReader = new SpriteReader(new ClientFeatures(false, true));
            reader.openSpriteFile(file);
            check(!reader.detectTransparency() && reader.transparencyKnown, "RGB input overrides a wrong transparency hint.");
            reader.closeSpriteFile();
            var info:ClientInfo = new ClientInfo(); info.features = new ClientFeatures(false, false);
            info.extendedKnown = true; info.transparencyKnown = true; info.hasMetadata = true; info.featureDescriptor = true;
            var selected:ClientFeatures = info.features.clone(); selected.transparency = true;
            check(AssetFeatureValidator.compare(info, selected).blocking, "Wrong Transparency blocks load before thumbnail errors.");
            selected.transparency = false;
            check(!AssetFeatureValidator.compare(info, selected).text, "Correct checkbox clears the warning.");
            selected.extended = true;
            check(AssetFeatureValidator.compare(info, selected).blocking, "Wrong Extended option blocks load.");
            selected = info.features.clone(); selected.improvedAnimations = true; selected.frameGroups = true;
            var warning:Object = AssetFeatureValidator.compare(info, selected);
            check(warning.text && !warning.blocking, "DAT descriptor hints warn without rejecting custom inputs.");
            info.transparencyKnown = false; selected = info.features.clone();
            check(!AssetFeatureValidator.compare(info, selected).blocking, "Ambiguous samples must not falsely block.");
            reader = new SpriteReader(new ClientFeatures(true, true)); reader.openSpriteFile(testRoot.resolvePath("all-empty.spr"));
            check(reader.detectTransparency() && !reader.transparencyKnown, "Empty data retains the hint without claiming detection.");
            reader.closeSpriteFile();
            file = testRoot.resolvePath("feature-rgba.spr"); featureFixture(file, true, true);
            reader = new SpriteReader(new ClientFeatures(true, false));
            reader.openSpriteFile(file);
            check(reader.detectTransparency() && reader.transparencyKnown, "Detect RGBA with the wrong initial hint.");
            reader.closeSpriteFile();
            var mixed:File = testRoot.resolvePath("feature-mixed.spr"); featureFixture(mixed, true, true);
            var edit:FileStream = new FileStream();
            try
            {
                edit.open(mixed, FileMode.UPDATE); edit.endian = Endian.LITTLE_ENDIAN;
                edit.position = 8 + 96 * 4 + 3; edit.writeShort(7); // First record RGB; the rest RGBA.
            }
            finally { edit.close(); }
            reader = new SpriteReader(new ClientFeatures(true, true)); reader.openSpriteFile(mixed); reader.detectTransparency();
            check(reader.transparencyConflict && !reader.transparencyKnown, "Mixed encodings must not be silently auto-corrected.");
            reader.closeSpriteFile(); info.transparencyConflict = true;
            check(AssetFeatureValidator.compare(info, selected).blocking, "Mixed archive warns before loading.");

            // First 32 entries empty: detection must also sample distant logical IDs.
            var sparse:File = testRoot.resolvePath("late-rgb.spr");
            featureFixture(sparse, true, false, 32);
            reader = new SpriteReader(new ClientFeatures(true, true)); reader.openSpriteFile(sparse);
            check(!reader.detectTransparency() && reader.transparencyKnown, "Distant RGB records detected after empty initial entries.");
            reader.closeSpriteFile();
            note("PASS: RGB/RGBA/header detection, red mismatches, yellow DAT hints, ambiguity, mixed encodings, sparse samples");
        }

        private function featureFixture(file:File, extended:Boolean, alpha:Boolean, emptyFirst:uint = 0):void
        {
            var stream:FileStream = new FileStream();
            try
            {
                stream.open(file, FileMode.WRITE); stream.endian = Endian.LITTLE_ENDIAN;
                stream.writeUnsignedInt(version().sprSignature);
                if (extended) stream.writeUnsignedInt(96); else stream.writeShort(96);
                var record:uint = (extended ? 8 : 6) + 96 * 4;
                for (var id:uint = 1; id <= 96; id++)
                    stream.writeUnsignedInt(id <= emptyFirst ? 0 : record + (id - emptyFirst - 1) * (alpha ? 13 : 12));
                for (id = emptyFirst + 1; id <= 96; id++)
                {
                    stream.writeByte(255); stream.writeByte(0); stream.writeByte(255); stream.writeShort(alpha ? 8 : 7);
                    stream.writeShort(0); stream.writeShort(1); stream.writeByte(10); stream.writeByte(20); stream.writeByte(30);
                    if (alpha) stream.writeByte(255);
                }
            }
            finally { stream.close(); }
        }

        private function writeText(file:File, text:String):void
        {
            var stream:FileStream = new FileStream();
            try { stream.open(file, FileMode.WRITE); stream.writeUTFBytes(text); } finally { stream.close(); }
        }
        private function readText(file:File):String
        {
            var stream:FileStream = new FileStream();
            try { stream.open(file, FileMode.READ); return stream.readUTFBytes(stream.bytesAvailable); }
            finally { stream.close(); }
            return "";
        }

        private function testExtendedItems():void
        {
            var f:ClientFeatures = new ClientFeatures(true); f.extendedItems = true;
            var source:File = testRoot.resolvePath("extended-items.dat");
            var writer:MetadataWriter = MetadataControllerStorage.getInstance().createWriter("default", 860);
            writer.features = f; writer.open(source, FileMode.WRITE);
            writer.writeUnsignedInt(version().datSignature); writer.writeUnsignedInt(65536);
            writer.writeShort(0); writer.writeShort(0); writer.writeShort(0);
            var settings:ObjectBuilderSettings = new ObjectBuilderSettings();
            for (var id:uint = 100; id <= 65536; id++)
            {
                var thing:ThingType = ThingType.create(id, ThingCategory.ITEM, false, settings.getDefaultDuration(ThingCategory.ITEM));
                writer.writeItemProperties(thing); writer.writeTexturePatterns(thing);
            }
            writer.close();
            var storage:ThingTypeStorage = new ThingTypeStorage(settings);
            storage.addEventListener(ErrorEvent.ERROR, capture);
            storage.load(source, version(), f);
            check(storage.loaded && storage.itemsCount == 65536, "32-bit DAT item count/load.");
            check(!storage.compile(testRoot.resolvePath("rejected-items.dat"), version(), new ClientFeatures(true)), "Reject item-count truncation.");
            check(storage.compile(testRoot.resolvePath("extended-items-out.dat"), version(), f), "32-bit DAT item output.");
            storage.unload();
            storage.load(testRoot.resolvePath("extended-items-out.dat"), version(), f);
            check(storage.loaded && storage.itemsCount == 65536, "Extended item count round-trip."); storage.unload();
            var bytes:ByteArray = new ByteArray(); f.writeExternal(bytes); bytes.position = 0;
            var copy:ClientFeatures = new ClientFeatures(); copy.readExternal(bytes);
            check(copy.extendedItems && !copy.differs(f), "Worker feature serialization preserves extended-items.");
            note("PASS: 65,536 DAT items, reject truncation, round-trip, worker feature serialization");
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
            note("Real features: " + f);
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
            var compileStarted:int = getTimer();
            check(sprites.compile(out.resolvePath("Tibia.spr"), v, f), "Real SPR merge: " + errorText);
            note("Real standard SPR compile: " + ((getTimer() - compileStarted) / 1000).toFixed(2) + "s");
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
            check(datReload.loaded, "Recompiled DAT reopen: " + errorText);
            check(datReload.itemsCount == probe.clientInfo.maxItemId && datReload.outfitsCount == probe.clientInfo.maxOutfitId,
                "Real DAT counts preserved.");
            datReload.unload();
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
