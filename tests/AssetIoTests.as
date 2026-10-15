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
    import otlib.sprites.SpriteRecordComparison;
    import otlib.sprites.MultiSpriteReader;
    import otlib.events.ProgressEvent;
    import otlib.resources.Resources;
    import otlib.sprites.SpriteReader;
    import otlib.sprites.SpriteStorage;
    import otlib.things.ThingTypeStorage;
    import otlib.utils.ClientInfoLoader;
    import otlib.utils.AssetDirectoryFinder;
    import otlib.utils.OTFI;
    import otlib.utils.AssetOutputTransaction;
    import otlib.utils.AssetFeatureValidator;
    import otlib.utils.ClientInfo;
    import otlib.utils.SpriteExtent;
    import otlib.storages.events.StorageEvent;
    import otlib.core.MetadataControllerStorage;
    import otlib.things.MetadataWriter;
    import otlib.things.ThingType;
    import otlib.things.ThingCategory;
    import otlib.utils.ThingSpriteMap;
    import otlib.utils.ClientMerger;
    import otlib.utils.SpritesOptimizer;
    import otlib.storages.StorageQueueLoader;

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
                testTransparentPixels();
                testFormats();
                testWindowRefill();
                testSequentialFileParts();
                testFeatureWarnings();
                testTransaction();
                testExtendedItems();
                testImportFailures();
                testQueueFailures();
                if (event.arguments.length > 1)
                {
                    if (event.arguments.length > 3 && event.arguments[3] == "probe-only")
                        testRealProbe(new File(event.arguments[1]), new File(event.arguments[2]));
                    else
                        testReal(new File(event.arguments[1]), new File(event.arguments[2]));
                }
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

        private function testTransparentPixels():void
        {
            // Transparent RGB is arbitrary: modern sheets use magenta, not just black.
            var pixels:ByteArray = new ByteArray(); pixels.length = 4096;
            for (var i:int = 0; i < 1024; i++) {
                pixels[i*4+1] = 255; pixels[i*4+3] = 255;
            }
            for each (var rgba:Boolean in [false,true]) {
                var sprite:otlib.sprites.Sprite = new otlib.sprites.Sprite(1,rgba);
                sprite.setPixels(pixels);
                check(sprite.isEmpty, "Alpha-zero magenta must be an empty SPR in RGB/RGBA.");
                pixels[4] = 128; pixels[5] = 12; pixels[6] = 34; pixels[7] = 56;
                pixels[4092] = 255; pixels[4093] = 255; pixels[4094] = 0; pixels[4095] = 255;
                sprite.setPixels(pixels);
                var decoded:ByteArray = sprite.getPixels();
                for (i = 0; i < 1024; i++) {
                    var offset:int = i*4;
                    if (pixels[offset] == 0) check(decoded[offset] == 0, "Transparent pixel became opaque: " + i);
                    else check(decoded[offset] == (rgba ? pixels[offset] : 255) && decoded[offset+1] == pixels[offset+1] && decoded[offset+2] == pixels[offset+2] && decoded[offset+3] == pixels[offset+3], "Visible pixel changed: " + i);
                }
                // A visible magenta pixel is legitimate; alpha, not RGB color-keying, decides.
                check(decoded[4092] == 255 && decoded[4093] == 255 && decoded[4095] == 255, "Visible magenta was removed.");
                if (rgba) { sprite.transparent = false; check(sprite.getPixels()[0] == 0, "RGBA to RGB restored a transparent background."); }
                sprite.dispose(); decoded.clear(); pixels[4] = 0; pixels[4092] = 0;
            }
            pixels.clear(); note("PASS: alpha-zero colored pixels stay transparent in RGB/RGBA SPR; visible colors remain intact");
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
            var lock:FileStream = new FileStream(); lock.open(unified, FileMode.READ);
            lastProgress = 0;
            compileEvents = 0; storage.addEventListener(StorageEvent.COMPILE, compiled);
            try { check(!storage.compile(unified, version(), f), "Locked SPR must not report success."); }
            finally { lock.close(); storage.removeEventListener(StorageEvent.COMPILE, compiled); }
            check(compileEvents == 0, "Locked SPR emits no success event.");
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
            // Reusing the same reader must replace both its format state and file handle.
            reader.openSpriteFile(source);
            reader.openSpriteFile(multiRoot.resolvePath("spr_index.dat"));
            var movedSource:File = testRoot.resolvePath("source-renamed.spr");
            new File(source.nativePath).moveTo(movedSource);
            check(movedSource.exists && !source.exists, "Switching to multi-SPR releases the old standard handle.");
            movedSource.moveTo(source);
            reader.openSpriteFile(testRoot.resolvePath("all-empty.spr"));
            check(reader.readSpriteCount() == 1 && reader.readSprite(1) == null, "Switching back removes the old multi-SPR reader.");
            reader.closeSpriteFile();
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
            try
            {
                reader.openSpriteFile(multiRoot.resolvePath("spr_index.dat"));
                reader.readSprite(1);
            }
            catch (error:Error) { bad = true; }
            finally { reader.closeSpriteFile(); }
            check(bad, "Reject invalid multi-SPR locator when that sprite is requested.");

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

        private function testSequentialFileParts():void
        {
            var root:File = testRoot.resolvePath("sequential-fileparts");
            var folder:File = root.resolvePath("FileParts");
            folder.createDirectory();
            sequentialPart(folder.resolvePath("part_1.spr"), 2, 10);
            sequentialPart(folder.resolvePath("part_2.spr"), 1, 90);
            var entry:File = SpriteReader.findMultiSpriteFile(root);
            check(entry && entry.name == "part_1.spr" && SpriteReader.isMultiSpriteFile(entry),
                "Detect index-less sequential FileParts input.");
            var reader:SpriteReader = new SpriteReader(new ClientFeatures(true, false));
            reader.openSpriteFile(entry);
            check(reader.readSignature() == version().sprSignature && reader.readSpriteCount() == 3,
                "Sequential FileParts aggregate count/signature.");
            check(reader.readSprite(1).getPixels()[1] == 10 && reader.readSprite(2) == null &&
                    reader.readSprite(3).getPixels()[1] == 90,
                "Sequential logical IDs cross part boundaries and keep empty records.");
            reader.closeSpriteFile();

            var gap:File = testRoot.resolvePath("sequential-gap/FileParts");
            gap.createDirectory();
            sequentialPart(gap.resolvePath("part_1.spr"), 1, 10);
            sequentialPart(gap.resolvePath("part_3.spr"), 1, 90);
            var rejected:Boolean = false;
            try { reader.openSpriteFile(gap.resolvePath("part_1.spr")); }
            catch (error:Error) { rejected = true; }
            finally { reader.closeSpriteFile(); }
            check(rejected, "Reject gaps in sequential FileParts instead of silently shifting IDs.");

            var incompleteRoot:File = testRoot.resolvePath("incomplete-multi");
            var incompleteParts:File = incompleteRoot.resolvePath("FileParts"); incompleteParts.createDirectory();
            sequentialPart(incompleteParts.resolvePath("part_1.spr"), 1, 10);
            write(incompleteRoot.resolvePath("spr_parts.dat"), new ByteArray());
            var multi:MultiSpriteReader = new MultiSpriteReader(false);
            var clearError:Boolean = false;
            try { multi.open(incompleteParts.resolvePath("part_1.spr")); }
            catch (error:Error) { clearError = error.message.indexOf("Incomplete multi-SPR set") >= 0; }
            finally { multi.close(); }
            check(clearError, "Incomplete indexed multi-SPR must report missing index/manifest clearly.");
            clearError = false;
            try { multi.open(null); }
            catch (missing:Error) { clearError = missing.message.indexOf("Missing multi-SPR index") >= 0; }
            finally { multi.close(); }
            check(clearError, "Null multi-SPR input must report a clear missing-entry error.");
        }

        private function sequentialPart(file:File, count:uint, color:uint):void
        {
            var bytes:ByteArray = new ByteArray(); bytes.endian = Endian.LITTLE_ENDIAN;
            bytes.writeUnsignedInt(version().sprSignature); bytes.writeUnsignedInt(count);
            var payload:Number = 8 + Number(count) * 4;
            bytes.writeUnsignedInt(payload);
            for (var i:uint = 2; i <= count; i++) bytes.writeUnsignedInt(0);
            bytes.writeByte(255); bytes.writeByte(0); bytes.writeByte(255);
            bytes.writeShort(7);
            bytes.writeShort(0); bytes.writeShort(1);
            bytes.writeByte(color); bytes.writeByte(20); bytes.writeByte(30);
            write(file, bytes);
        }

        private function testWindowRefill():void
        {
            var count:uint = 300;
            var bytes:ByteArray = new ByteArray(); bytes.endian = Endian.LITTLE_ENDIAN;
            bytes.writeUnsignedInt(version().sprSignature); bytes.writeUnsignedInt(count);
            for (var id:uint = 1; id <= count; id++) bytes.writeUnsignedInt(0);
            for (id = 1; id <= count; id++)
            {
                var address:uint = bytes.length;
                var returnPosition:uint = bytes.position;
                bytes.position = 8 + Number(id - 1) * 4;
                bytes.writeUnsignedInt(address);
                bytes.position = returnPosition;
                bytes.writeByte(255); bytes.writeByte(0); bytes.writeByte(255);
                bytes.writeShort(4100); // 4-byte run header + 1,024 RGBA pixels
                bytes.writeShort(0); bytes.writeShort(1024);
                for (var pixel:uint = 0; pixel < 1024; pixel++)
                {
                    bytes.writeByte(0x12); bytes.writeByte(0x34); bytes.writeByte(0x56); bytes.writeByte(0xFF);
                }
            }
            var file:File = testRoot.resolvePath("window-refill.spr"); write(file, bytes);
            var features:ClientFeatures = new ClientFeatures(true, true);
            var reader:SpriteReader = new SpriteReader(features);
            reader.openSpriteFile(file);
            try
            {
                for (id = 1; id <= count; id++)
                {
                    var sprite:otlib.sprites.Sprite = reader.readSprite(id);
                    check(sprite != null, "Dense window fixture sprite missing: " + id);
                    try
                    {
                        var decoded:ByteArray = sprite.getPixels();
                        check(decoded.length == SpriteExtent.DEFAULT_DATA_SIZE && decoded[0] == 0xFF && decoded[1] == 0x12 &&
                            decoded[2] == 0x34 && decoded[3] == 0x56,
                            "Window fixture pixel mismatch at " + id + ": length=" + decoded.length +
                            " rgba=" + decoded[0] + "," + decoded[1] + "," + decoded[2] + "," + decoded[3]);
                    }
                    finally { sprite.dispose(); }
                }
            }
            finally { reader.closeSpriteFile(); }
            note("PASS: sprite payload reads remain correct across a 1 MiB file-window refill");
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
        private function write(file:File, bytes:ByteArray):void
        {
            var stream:FileStream = new FileStream();
            try { stream.open(file, FileMode.WRITE); stream.writeBytes(bytes); } finally { stream.close(); }
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
            storage.addEventListener(StorageEvent.COMPILE, compiled);
            compileEvents = 0;
            check(!storage.compile(testRoot.resolvePath("rejected-items.dat"), version(), new ClientFeatures(true)), "Reject item-count truncation.");
            check(compileEvents == 0, "Rejected DAT compile must not emit success.");
            check(storage.compile(testRoot.resolvePath("extended-items-out.dat"), version(), f), "32-bit DAT item output.");
            var lock:FileStream = new FileStream();
            lock.open(testRoot.resolvePath("extended-items-out.dat"), FileMode.READ);
            compileEvents = 0;
            try { check(!storage.compile(testRoot.resolvePath("extended-items-out.dat"), version(), f), "Locked DAT must not report success."); }
            finally { lock.close(); }
            check(compileEvents == 0, "Locked DAT emits no success event.");
            storage.unload();
            storage.load(testRoot.resolvePath("extended-items-out.dat"), version(), f);
            check(storage.loaded && storage.itemsCount == 65536, "Extended item count round-trip."); storage.unload();
            var bytes:ByteArray = new ByteArray(); f.writeExternal(bytes); bytes.position = 0;
            var copy:ClientFeatures = new ClientFeatures(); copy.readExternal(bytes);
            check(copy.extendedItems && !copy.differs(f), "Worker feature serialization preserves extended-items.");
            note("PASS: 65,536 DAT items, reject truncation, round-trip, worker feature serialization");
        }

        private function testQueueFailures():void
        {
            var queue:StorageQueueLoader = new StorageQueueLoader();
            queue.start(); check(queue.loaded && !queue.running, "Empty queue completes.");
            var storage:SpriteStorage = new SpriteStorage();
            var next:SpriteStorage = new SpriteStorage();
            queue = new StorageQueueLoader();
            var failures:uint = 0, calls:uint = 0;
            queue.addEventListener(ErrorEvent.ERROR, function(e:ErrorEvent):void { failures++; });
            queue.add(storage, function():void { storage.dispatchEvent(new ErrorEvent(ErrorEvent.ERROR, false, false, "fixture failure")); });
            queue.add(next, function():void { calls++; });
            queue.start(); storage.dispatchEvent(new StorageEvent(StorageEvent.LOAD));
            check(failures == 1 && calls == 0 && !queue.running && !queue.loaded, "Failed queue stops and ignores stale completions.");
            queue = new StorageQueueLoader();
            queue.addEventListener(ErrorEvent.ERROR, function(e:ErrorEvent):void { failures++; });
            queue.add(storage, function():void { throw new Error("fixture exception"); });
            queue.start(); check(failures == 2 && !queue.running, "Thrown load also terminates the queue.");
            queue = new StorageQueueLoader(); calls = 0;
            queue.add(storage, function():void { calls++; storage.dispatchEvent(new StorageEvent(StorageEvent.LOAD)); });
            queue.start(); queue.start();
            check(queue.loaded && calls == 2, "Completed queue can be reused without duplicate callbacks.");
            note("PASS: empty, failed, thrown, stale and reused storage queues");
        }

        private function testImportFailures():void
        {
            var f:ClientFeatures = new ClientFeatures(true);
            var source:File = testRoot.resolvePath("strict-source.spr"); featureFixture(source, true, false);
            var sprites:SpriteStorage = new SpriteStorage(); sprites.load(source, version(), f);
            check(sprites.getPixelsStrict(1)[1] == 10, "Strict read of valid RGB.");
            check(!sprites.changed && sprites.replaceSprite(1, sprites.getPixelsStrict(1)).done && sprites.changed,
                "Sprite mutations update dirty state without requiring an event listener.");
            var thing:ThingType = ThingType.create(100, ThingCategory.ITEM, false, 100);
            thing.getFrameGroup(0).spriteIndex[0] = 1;
            var mapped:ThingType = ThingSpriteMap.remap(thing, {"1": 42});
            check(mapped.getFrameGroup(0).spriteIndex[0] == 42 && thing.getFrameGroup(0).spriteIndex[0] == 1, "Remapping preserves source.");
            var rejected:Boolean = false;
            try { ThingSpriteMap.remap(thing, {}); } catch (e:Error) { rejected = true; }
            check(rejected, "Missing mappings must reject, not retain foreign sprite IDs.");
            var settings:ObjectBuilderSettings = new ObjectBuilderSettings();
            var things:ThingTypeStorage = new ThingTypeStorage(settings); things.createNew(version(), f);
            things.replaceThing(thing, ThingCategory.ITEM, 100);
            var dat:File = testRoot.resolvePath("merge-source.dat");
            check(things.compile(dat, version(), f), "Merge DAT fixture.");
            var full:SpriteStorage = new SpriteStorage();
            full.load(testRoot.resolvePath("legacy.spr"), version(), new ClientFeatures(false));
            var merger:ClientMerger = new ClientMerger(things, full, settings);
            var failures:uint = 0, completions:uint = 0;
            merger.addEventListener(ErrorEvent.ERROR, function(e:ErrorEvent):void { failures++; });
            merger.addEventListener(Event.COMPLETE, function(e:Event):void { completions++; });
            merger.start(dat, source, version(), f, false);
            check(failures == 1 && completions == 0 && things.itemsCount == 100 && full.spritesCount == 65535,
                "Capacity failure must not create objects with incorrect sprite IDs.");
            full.unload(); sprites.unload();
            var edit:FileStream = new FileStream();
            try { edit.open(source, FileMode.UPDATE); edit.endian = Endian.LITTLE_ENDIAN; edit.position = 8 + 96 * 4 + 7; edit.writeShort(2000); }
            finally { edit.close(); }
            sprites.load(source, version(), f); rejected = false;
            try { sprites.getPixelsStrict(1); } catch (e:Error) { rejected = true; }
            check(rejected, "Corrupt RLE must not be imported as a preview placeholder.");
            thing.getFrameGroup(0).spriteIndex[0] = 999;
            things.replaceThing(thing, ThingCategory.ITEM, 100); rejected = false;
            try { new SpritesOptimizer(things, sprites).start(); } catch (e:Error) { rejected = true; }
            check(rejected && things.getThingType(100, ThingCategory.ITEM).getFrameGroup(0).spriteIndex[0] == 999,
                "Optimizer rejects invalid references before modifying the document.");
            sprites.unload(); things.unload();
            note("PASS: strict RLE import, missing mapping, full legacy merge, optimizer reference preflight");
        }

        private function testReal(source:File, config:File):void
        {
            source = AssetDirectoryFinder.resolve(source);
            VersionStorage.getInstance().load(config.resolvePath("versions.xml"));
            var probe:ClientInfoLoader = new ClientInfoLoader();
            probe.addEventListener(ErrorEvent.ERROR, capture);
            var inputSpr:File = source.resolvePath("Tibia.spr");
            if (!inputSpr.exists) inputSpr = SpriteReader.findMultiSpriteFile(source);
            probe.load(source.resolvePath("Tibia.dat"), inputSpr, true);
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
            sprites.load(inputSpr, v, f);
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
            var before:SpriteReader = new SpriteReader(f), after:SpriteReader = new SpriteReader(f);
            before.openSpriteFile(inputSpr); after.openSpriteFile(out.resolvePath("Tibia.spr"));
            try {
                for (id = 1; id <= sprites.spritesCount; id++) {
                    var a:otlib.sprites.Sprite = before.readSprite(id), b:otlib.sprites.Sprite = after.readSprite(id);
                    try {
                        check(SpriteRecordComparison.equal(a, b), "Real compressed sprite record changed: " + id);
                    } finally { if (a) a.dispose(); if (b) b.dispose(); }
                }
            } finally { before.closeSpriteFile(); after.closeSpriteFile(); }
            note("PASS: all " + sprites.spritesCount + " real SPR records validated and preserved byte-for-byte");
            var edited:ByteArray=reopened.getPixelsStrict(1);edited[0]=255;edited[1]=17;edited[2]=31;edited[3]=59;
            check(reopened.replaceSprite(1,edited).done,"Real legacy pixel edit");
            var changedThing:ThingType=things.getThingType(100,ThingCategory.ITEM).clone();changedThing.isUnpassable=!changedThing.isUnpassable;
            check(things.replaceThing(changedThing,ThingCategory.ITEM,100).done,"Real legacy property edit");
            var editedDat:File=out.resolvePath("Edited.dat"),editedSpr:File=out.resolvePath("Edited.spr");
            check(things.compile(editedDat,v,f) && reopened.compile(editedSpr,v,f),"Real edited DAT/SPR save");
            var editedRead:SpriteStorage=new SpriteStorage();editedRead.load(editedSpr,v,f);check(samePixels(editedRead.getPixelsStrict(1),edited),"Edited standard SPR reopen");editedRead.unload();
            var editedThings:ThingTypeStorage=new ThingTypeStorage(new ObjectBuilderSettings());editedThings.load(editedDat,v,f);
            check(editedThings.loaded && editedThings.getThingType(100,ThingCategory.ITEM).isUnpassable==changedThing.isUnpassable,"Edited DAT property reopen");editedThings.unload();
            var multiOut:File=testRoot.resolvePath("real-multi");multiOut.createDirectory();var multiFeatures:ClientFeatures=f.clone();
            check(things.compile(multiOut.resolvePath("Tibia.dat"),v,multiFeatures),"Real multi DAT compile");
            check(reopened.compile(multiOut.resolvePath("spr_index.dat"),v,multiFeatures),"Real edited multi-SPR compile");
            var multiRead:SpriteStorage=new SpriteStorage();multiRead.load(multiOut.resolvePath("spr_index.dat"),v,multiFeatures);
            check(multiRead.loaded && multiRead.spritesCount==sprites.spritesCount && samePixels(multiRead.getPixelsStrict(1),edited),"Real multi-SPR edit/reopen");
            for each(var boundary:uint in Vector.<uint>([65535,65536,sprites.spritesCount]))if(boundary<=sprites.spritesCount)check(samePixels(multiRead.getPixelsStrict(boundary),sprites.getPixelsStrict(boundary)),"Real multi-SPR boundary "+boundary);
            var back:File=testRoot.resolvePath("real-multi-back.spr");var standardFeatures:ClientFeatures=f.clone();
            check(multiRead.compile(back,v,standardFeatures),"Real multi-SPR to standard compile");multiRead.unload();
            var backRead:SpriteStorage=new SpriteStorage();backRead.load(back,v,standardFeatures);check(samePixels(backRead.getPixelsStrict(1),edited),"Real multi-SPR to standard edited-pixel reopen");backRead.unload();
            edited.clear();note("PASS: real legacy sprite/property edit-save-reopen and full multi-SPR compile/reopen/standard round trip");
            reopened.unload(); sprites.unload(); things.unload();
            var datReload:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings());
            datReload.addEventListener(ErrorEvent.ERROR, capture); datReload.load(out.resolvePath("Tibia.dat"), v, f);
            check(datReload.loaded, "Recompiled DAT reopen: " + errorText);
            check(datReload.itemsCount == probe.clientInfo.maxItemId && datReload.outfitsCount == probe.clientInfo.maxOutfitId,
                "Real DAT counts preserved.");
            datReload.unload();
            note("PASS: real DAT/SPR compile, sampled pixels, reopen and close");
        }

        /** Read-only real-client validation: intentionally does not write or merge assets. */
        private function testRealProbe(selected:File, config:File):void
        {
            var source:File = AssetDirectoryFinder.resolve(selected);
            check(source && source.exists && source.isDirectory, "Client folder not found.");
            VersionStorage.getInstance().load(config.resolvePath("versions.xml"));
            var inputSpr:File = source.resolvePath("Tibia.spr");
            if (!inputSpr.exists) inputSpr = SpriteReader.findMultiSpriteFile(source);
            check(inputSpr != null, "Neither Tibia.spr nor a supported multi-SPR archive was found.");
            note("Resolved real client: " + source.nativePath);
            note("Resolved real SPR: " + inputSpr.nativePath);
            var probe:ClientInfoLoader = new ClientInfoLoader();
            probe.addEventListener(ErrorEvent.ERROR, capture);
            probe.load(source.resolvePath("Tibia.dat"), inputSpr, true);
            check(probe.clientInfo != null, "Real file probe: " + errorText);
            var info:ClientInfo = probe.clientInfo;
            check(info.clientVersion == 860, "Expected 8.60, detected " + info.clientVersionStr + ".");
            check(info.multiSpr && info.features.extended, "Expected extended multi-SPR assets.");
            var v:Version = VersionStorage.getInstance().getFromClientInfo(info);
            check(v != null, "Could not resolve client version from DAT/SPR signatures.");
            var things:ThingTypeStorage = new ThingTypeStorage(new ObjectBuilderSettings());
            things.addEventListener(ErrorEvent.ERROR, capture);
            things.load(source.resolvePath("Tibia.dat"), v, info.features);
            check(things.loaded, "Real DAT load: " + errorText);
            note("PASS: real 8.60 DAT loaded (items " + things.itemsCount + ", outfits " + things.outfitsCount + ")");

            var sprites:SpriteStorage = new SpriteStorage();
            sprites.addEventListener(ErrorEvent.ERROR, capture);
            sprites.load(inputSpr, v, info.features);
            check(sprites.loaded && sprites.spritesCount == info.maxSpriteId,
                "Real multi-SPR logical count mismatch: " + errorText);
            if (SpriteReader.isFilePartsEntry(inputSpr) && sprites.spritesCount >= 524)
            {
                var directPart:File = testRoot.resolvePath("direct-part-1.spr");
                inputSpr.parent.resolvePath("part_1.spr").copyTo(directPart, true);
                var directReader:SpriteReader = new SpriteReader(info.features);
                try
                {
                    directReader.openSpriteFile(directPart);
                    for (var directId:uint = 1; directId <= 524; directId++)
                    {
                        var directRecord:otlib.sprites.Sprite = directReader.readSprite(directId);
                        if (directRecord)
                        {
                            try { directRecord.getPixels(); }
                            finally { directRecord.dispose(); }
                        }
                    }
                }
                finally { directReader.closeSpriteFile(); }
                note("PASS: direct part_1 sprite 524 decoded outside the multi-SPR aggregator");
            }
            var reader:SpriteReader = new SpriteReader(info.features);
            var decoded:uint = 0;
            try
            {
                reader.openSpriteFile(inputSpr);
                for (var spriteId:uint = 1; spriteId <= sprites.spritesCount; spriteId++)
                {
                    var record:otlib.sprites.Sprite = reader.readSprite(spriteId);
                    if (record)
                    {
                        try
                        {
                            var pixels:ByteArray = record.getPixels();
                            check(pixels && pixels.length == SpriteExtent.DEFAULT_DATA_SIZE,
                                "Invalid decoded pixel buffer for sprite " + spriteId);
                            pixels.clear();
                        }
                        finally { record.dispose(); }
                        decoded++;
                    }
                    if (spriteId % 65536 == 0) note("Validated real SPR records through " + spriteId + "/" + sprites.spritesCount);
                }
            }
            finally { reader.closeSpriteFile(); }
            note("PASS: every real SPR ID decoded (" + decoded + " non-empty records of " + sprites.spritesCount + ")");
            sprites.unload(); things.unload();
        }

        private function samePixels(a:ByteArray, b:ByteArray):Boolean
        {
            if (!a || !b || a.length != b.length) return false;
            for (var i:uint = 0; i < a.length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
