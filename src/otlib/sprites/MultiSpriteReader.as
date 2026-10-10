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
        private var _readers:Object = {};
        private var _lru:Array = [];

        public function MultiSpriteReader(transparent:Boolean) { _transparent = transparent; }

        public function open(indexFile:File, progress:Function = null):void
        {
            close();
            try
            {
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
                        reader.openSpriteFile(file);
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
                if (progress != null) progress(0, count, "Loading SPR index");
                var lastProgress:int = getTimer();
                for (var id:uint = 1; id <= count; id++)
                {
                    var locator:uint = _index.readUnsignedInt();
                    var part:uint = locator >>> 16;
                    var local:uint = locator & 65535;
                    if (!part || part > num || !local || local > _counts[part - 1])
                        throw new Error("Invalid multi-SPR locator at logical sprite " + id);
                    if (progress != null && (getTimer() - lastProgress >= 100 || id == count))
                    {
                        progress(id, count, "Loading SPR index");
                        lastProgress = getTimer();
                    }
                }
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
            if (!_index || !id || id > count) throw new RangeError("Invalid multi-SPR id: " + id);
            _index.position = 12 + Number(id - 1) * 4;
            var locator:uint = _index.readUnsignedInt();
            var part:uint = locator >>> 16;
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
                reader.openSpriteFile(_files[part - 1]);
                _readers[part] = reader;
            }
            var at:int = _lru.indexOf(part);
            if (at >= 0) _lru.splice(at, 1);
            _lru.push(part);
            var sprite:Sprite = reader.readSpriteRecord(locator & 65535);
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
    }
}
