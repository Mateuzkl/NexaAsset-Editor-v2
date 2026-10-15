package otlib.sprites
{
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.utils.ByteArray;
    import flash.utils.Endian;
    import flash.utils.getTimer;
    import otlib.core.ClientFeatures;

    /** Rust-compatible SPMT/SPIX v1; global IDs never depend on physical ordering. */
    public final class MultiSpriteReader
    {
        public var signature:uint;
        public var count:uint;
        private var _transparent:Boolean;
        private var _index:ByteArray;
        private var _files:Array = [];
        private var _counts:Array = [];
        private var _ends:Array = [];
        private var _readers:Object = {};
        private var _lru:Array = [];
        private var _sequential:Boolean;

        public function MultiSpriteReader(transparent:Boolean) { _transparent = transparent; }

        public function open(indexFile:File, progress:Function = null):void
        {
            close();
            try
            {
                if (!indexFile)
                    throw new Error("Missing multi-SPR index or FileParts entry.");
                var normalizedFile:File = SpriteReader.normalizeMultiSpriteFile(indexFile);
                if (!normalizedFile)
                    throw new Error("Incomplete multi-SPR set: spr_index.dat and spr_parts.dat must both exist beside FileParts/part_1.spr.");
                indexFile = normalizedFile;
                if (!indexFile.exists || indexFile.isDirectory)
                    throw new Error("Missing multi-SPR index or FileParts entry: " + indexFile.nativePath);
                if (SpriteReader.isFilePartsEntry(indexFile))
                {
                    openSequentialParts(indexFile, progress);
                    return;
                }
                if (!indexFile.parent)
                    throw new Error("Invalid multi-SPR index path: no containing folder.");
                var root:File = indexFile.parent;
                var parts:ByteArray = readFile(root.resolvePath("spr_parts.dat"));
                if (parts.length < 8 || parts.readUTFBytes(4) != "SPMT")
                    throw new Error("Invalid multi-SPR manifest (expected SPMT).");
                var num:uint = parts.readUnsignedInt();
                if (!num || num > 10000 || parts.length != 8 + Number(num) * 8)
                    throw new Error("Invalid multi-SPR part count/manifest length.");
                var total:Number = 0;
                for (var p:uint = 1; p <= num; p++)
                {
                    var sig:uint = parts.readUnsignedInt();
                    var n:uint = parts.readUnsignedInt();
                    if (!n || n > 65535) throw new Error("Invalid sprite count in part " + p);
                    if (p == 1) signature = sig;
                    if (sig != signature) throw new Error("Multi-SPR signatures differ.");
                    var file:File = resolvePart(root, p);
                    var reader:SpriteReader = new SpriteReader(ClientFeatures.create(true, _transparent));
                    try
                    {
                        reader.openStandardSpriteFile(file);
                        if (reader.readSignature() != sig || reader.readSpriteCount() != n)
                            throw new Error("Multi-SPR header/manifest mismatch: " + file.name);
                    }
                    finally { reader.closeSpriteFile(); }
                    _files.push(file);
                    _counts.push(n);
                    total += n;
                }
                _index = readFile(indexFile);
                if (_index.length < 12 || _index.readUTFBytes(4) != "SPIX" || _index.readUnsignedInt() != 1)
                    throw new Error("Invalid multi-SPR index (expected SPIX version 1).");
                count = _index.readUnsignedInt();
                if (!count || count != total || _index.length != 12 + Number(count) * 4)
                    throw new Error("Multi-SPR logical count/index length mismatch.");
                // Locator validation is lazy in readSpriteRecord(). Walking every
                // entry here blocks the UI for large indexed archives (1M+ sprites).
                if (progress != null) progress(count, count, "SPR index ready");
            }
            catch (error:Error) { close(); throw error; }
        }

        public function readSprite(id:uint):Sprite
        {
            var sprite:Sprite = readSpriteRecord(id);
            if (sprite) sprite.validateCompressedPixels();
            return sprite;
        }

        internal function readSpriteRecord(id:uint):Sprite
        {
            if ((!_index && !_sequential) || !id || id > count) throw new RangeError("Invalid multi-SPR id: " + id);
            var part:uint, local:uint;
            if (_index)
            {
                _index.position = 12 + Number(id - 1) * 4;
                var locator:uint = _index.readUnsignedInt();
                part = locator >>> 16;
                local = locator & 65535;
                if (!part || part > _files.length || !local || local > uint(_counts[part - 1]))
                    throw new Error("Invalid multi-SPR locator at logical sprite " + id);
            }
            else
            {
                var low:int = 0, high:int = _ends.length - 1;
                while (low < high)
                {
                    var middle:int = (low + high) >>> 1;
                    if (id <= uint(_ends[middle])) high = middle;
                    else low = middle + 1;
                }
                part = uint(low + 1);
                var previousEnd:uint = part > 1 ? uint(_ends[part - 2]) : 0;
                local = id - previousEnd;
            }
            var reader:SpriteReader = _readers[part];
            if (!reader)
            {
                // Bounded open handles, including random cross-part previews.
                if (_lru.length == 4)
                {
                    var oldest:uint = _lru.shift();
                    SpriteReader(_readers[oldest]).closeSpriteFile();
                    delete _readers[oldest];
                }
                reader = new SpriteReader(ClientFeatures.create(true, _transparent));
                reader.openStandardSpriteFile(_files[part - 1]);
                _readers[part] = reader;
            }
            var at:int = _lru.indexOf(part);
            if (at >= 0) _lru.splice(at, 1);
            _lru.push(part);
            var sprite:Sprite = reader.readSpriteRecord(local);
            if (sprite) sprite.id = id;
            return sprite;
        }

        internal function setTransparency(value:Boolean):void
        {
            for each (var reader:SpriteReader in _readers) reader.closeSpriteFile();
            _readers = {}; _lru = [];
            _transparent = value;
        }

        public function close():void
        {
            for each (var reader:SpriteReader in _readers) reader.closeSpriteFile();
            _readers = {};
            _lru = [];
            _files = [];
            _counts = [];
            _ends = [];
            _sequential = false;
            if (_index) _index.clear();
            _index = null;
            count = 0;
            signature = 0;
        }

        private static function readFile(file:File):ByteArray
        {
            if (!file.exists || file.size > 0x7FFFFFFF) throw new Error("Missing/oversized index: " + file.nativePath);
            var bytes:ByteArray = new ByteArray();
            bytes.endian = Endian.LITTLE_ENDIAN;
            var stream:FileStream = new FileStream();
            try { stream.open(file, FileMode.READ); stream.readBytes(bytes); }
            finally { try { stream.close(); } catch (error:Error) {} }
            return bytes;
        }

        private static function resolvePart(root:File, part:uint):File
        {
            var padded:String = ("000" + part).substr(-3);
            var names:Array = ["part_" + part + ".spr", "spr_" + padded + ".dat",
                "part_" + padded + ".spr", "spr_" + part + ".dat"];
            for each (var name:String in names)
                for each (var dir:String in ["FileParts/", ""])
                {
                    var file:File = root.resolvePath(dir + name);
                    if (file.exists && !file.isDirectory) return file;
                }
            throw new Error("Missing multi-SPR part " + part + " in " + root.nativePath);
        }

        /** Older 8.x clients store consecutive 32-bit SPR chunks without SPIX/SPMT indexes. */
        private function openSequentialParts(firstPart:File, progress:Function):void
        {
            var folder:File = firstPart.parent;
            if (!folder.exists || !folder.isDirectory)
                throw new Error("Missing FileParts directory: " + folder.nativePath);
            var files:Object = {};
            var maximum:uint = 0;
            var listing:Array = folder.getDirectoryListing();
            for each (var entry:File in listing)
            {
                var matched:Array = /^part_(\d+)\.spr$/i.exec(entry.name);
                if (!matched || entry.isDirectory) continue;
                var number:Number = Number(matched[1]);
                if (!number || number > 10000 || number % 1 != 0)
                    throw new Error("Invalid sequential SPR part name: " + entry.name);
                var partNumber:uint = uint(number);
                if (files[partNumber]) throw new Error("Duplicate sequential SPR part: " + entry.name);
                files[partNumber] = entry;
                maximum = Math.max(maximum, partNumber);
            }
            if (!maximum || !files[1]) throw new Error("Missing FileParts/part_1.spr.");

            var total:Number = 0;
            var lastProgress:int = getTimer();
            _sequential = true;
            for (var part:uint = 1; part <= maximum; part++)
            {
                var file:File = files[part] as File;
                if (!file) throw new Error("Missing sequential SPR part " + part + ".");
                var reader:SpriteReader = new SpriteReader(ClientFeatures.create(true, _transparent));
                try
                {
                    reader.openStandardSpriteFile(file);
                    var sig:uint = reader.readSignature();
                    var sprites:uint = reader.readSpriteCount();
                    if (!sprites || sprites > 65535)
                        throw new Error("Invalid sprite count in part " + part + ".");
                    if (part == 1) signature = sig;
                    if (sig != signature) throw new Error("Sequential SPR signatures differ at part " + part + ".");
                    total += sprites;
                    if (total > 0xFFFFFFFF) throw new Error("Sequential SPR logical count exceeds 32-bit IDs.");
                    _files.push(file);
                    _counts.push(sprites);
                    _ends.push(total);
                }
                finally { reader.closeSpriteFile(); }
                if (progress != null && (getTimer() - lastProgress >= 100 || part == maximum))
                {
                    progress(part, maximum, "Loading SPR parts");
                    lastProgress = getTimer();
                }
            }
            count = uint(total);
            if (!count) throw new Error("Sequential SPR has no sprites.");
        }
    }
}
