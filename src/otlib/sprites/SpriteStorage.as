/*
*  Copyright (c) 2014-2023 Object Builder <https://github.com/ottools/ObjectBuilder>
*
*  Permission is hereby granted, free of charge, to any person obtaining a copy
*  of this software and associated documentation files (the "Software"), to deal
*  in the Software without restriction, including without limitation the rights
*  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
*  copies of the Software, and to permit persons to whom the Software is
*  furnished to do so, subject to the following conditions:
*
*  The above copyright notice and this permission notice shall be included in
*  all copies or substantial portions of the Software.
*
*  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
*  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
*  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
*  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
*  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
*  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
*  THE SOFTWARE.
*/

package otlib.sprites
{
    import flash.display.BitmapData;
    import flash.events.ErrorEvent;
    import flash.events.EventDispatcher;
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.geom.Point;
    import flash.geom.Rectangle;
    import flash.utils.ByteArray;
    import flash.utils.Dictionary;
    import flash.utils.Endian;
    import flash.utils.getTimer;

    import nail.errors.NullArgumentError;
    import nail.logging.Log;
    import nail.utils.FileUtil;

    import ob.commands.ProgressBarID;

    import otlib.assets.Assets;
    import otlib.core.Version;
    import otlib.core.ClientFeatures;
    import otlib.core.otlib_internal;
    import otlib.events.ProgressEvent;
    import otlib.resources.Resources;
    import otlib.storages.IStorage;
    import otlib.storages.events.StorageEvent;
    import otlib.utils.ChangeResult;
    import otlib.utils.SpriteUtils;
    import otlib.utils.SpriteExtent;
    import otlib.utils.SpriteRetiler;
    import otlib.utils.AssetOutputTransaction;
    import otlib.core.VersionStorage;
    import otlib.things.ThingType;

    use namespace otlib_internal;

    [Event(name="progress", type="flash.events.ProgressEvent")]
    [Event(name="load", type="otlib.storages.events.StorageEvent")]
    [Event(name="compile", type="otlib.storages.events.StorageEvent")]
    [Event(name="change", type="otlib.storages.events.StorageEvent")]
    [Event(name="unloading", type="otlib.storages.events.StorageEvent")]
    [Event(name="unload", type="otlib.storages.events.StorageEvent")]
    [Event(name="error", type="flash.events.ErrorEvent")]

    public class SpriteStorage extends EventDispatcher implements IStorage
    {
        // --------------------------------------------------------------------------
        // PROPERTIES
        // --------------------------------------------------------------------------

        otlib_internal var _sprites:Dictionary;
        otlib_internal var _spritesCount:uint;

        private var _file:File;
        private var _reader:SpriteReader;
        private var _version:Version;
        private var _signature:uint;
        private var _rect:Rectangle;
        private var _point:Point;
        private var _blankSprite:Sprite;
        private var _alertSprite:Sprite;
        private var _headerSize:uint;
        otlib_internal var _changed:Boolean;
        private var _loaded:Boolean;
        private var _currentFeatures:ClientFeatures;
        private var _rgbDataBuffer:ByteArray;
        private var _exporting:Boolean;

        // --------------------------------------
        // Getters / Setters
        // --------------------------------------

        public function get file():File
        {
            return _file;
        }
        public function get version():Version
        {
            return _version;
        }
        public function get signature():uint
        {
            return _signature;
        }
        public function get spritesCount():uint
        {
            return _spritesCount;
        }
        public function get loaded():Boolean
        {
            return _loaded;
        }
        public function get changed():Boolean
        {
            return _changed;
        }
        public function get isFull():Boolean
        {
            return (_currentFeatures && !_currentFeatures.extended && _spritesCount >= 0xFFFF) || _spritesCount == uint.MAX_VALUE;
        }
        public function get transparency():Boolean
        {
            return _currentFeatures ? _currentFeatures.transparency : false;
        }

        public function get alertSprite():Sprite
        {
            return _alertSprite;
        }
        public function get isTemporary():Boolean
        {
            return (_loaded && _file == null);
        }

        // --------------------------------------------------------------------------
        // CONSTRUCTOR
        // --------------------------------------------------------------------------

        public function SpriteStorage()
        {
            _rect = new Rectangle(0, 0, SpriteExtent.DEFAULT_SIZE, SpriteExtent.DEFAULT_SIZE);
            _point = new Point();
        }

        // --------------------------------------------------------------------------
        // METHODS
        // --------------------------------------------------------------------------

        // --------------------------------------
        // Public
        // --------------------------------------

        public function load(file:File, version:Version, features:ClientFeatures):void
        {
            if (!file)
                throw new NullArgumentError("file");

            if (!version)
                throw new NullArgumentError("version");

            if (loaded)
                return;

            onLoad(file, version, features, false);
        }

        public function createNew(version:Version, features:ClientFeatures):void
        {
            if (!version)
                throw new NullArgumentError("version");

            if (this.loaded)
                return;

            _version = version;
            _currentFeatures = features.clone();
            _currentFeatures.applyVersionDefaults(version.value);
            _signature = version.sprSignature;
            _spritesCount = 1;

            _headerSize = _currentFeatures.extended ? SpriteFileSize.HEADER_U32 : SpriteFileSize.HEADER_U16;

            _blankSprite = new Sprite(0, _currentFeatures.transparency);
            _alertSprite = createAlertSprite(_currentFeatures.transparency);
            _sprites = new Dictionary();
            _sprites[0] = _blankSprite;
            _sprites[1] = new Sprite(1, _currentFeatures.transparency);
            _changed = false;
            _loaded = true;

            dispatchEvent(new StorageEvent(StorageEvent.LOAD));
        }

        public function addSprite(pixels:ByteArray):ChangeResult
        {
            if (!pixels)
            {
                throw new NullArgumentError("pixels");
            }

            var result:ChangeResult = internalAddSprite(pixels);
            _changed = _changed || result.done;
            if (result.done && hasEventListener(StorageEvent.CHANGE))
            {
                _changed = true;
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }

            return result;
        }

        public function addSprites(sprites:Vector.<ByteArray>):ChangeResult
        {
            if (!sprites)
            {
                throw new NullArgumentError("sprites");
            }

            var result:ChangeResult = internalAddSprites(sprites);
            _changed = _changed || result.done;
            if (result.done && hasEventListener(StorageEvent.CHANGE))
            {
                _changed = true;
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }

            return result;
        }

        public function replaceSprite(id:uint, pixels:ByteArray):ChangeResult
        {
            if (id == 0 || id > _spritesCount)
            {
                throw new ArgumentError(Resources.getString("indexOutOfRange"));
            }

            if (!pixels)
            {
                throw new NullArgumentError("pixels");
            }

            if (pixels.length != SpriteExtent.DEFAULT_DATA_SIZE)
            {
                throw new ArgumentError("Parameter pixels has an invalid length.");
            }

            var result:ChangeResult = internalReplaceSprite(id, pixels);
            _changed = _changed || result.done;
            if (result.done && hasEventListener(StorageEvent.CHANGE))
            {
                _changed = true;
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }

            return result;
        }

        public function replaceSprites(sprites:Vector.<SpriteData>):ChangeResult
        {
            if (!sprites)
            {
                throw new NullArgumentError("sprites");
            }

            var result:ChangeResult = internalReplaceSprites(sprites);
            _changed = _changed || result.done;
            if (result.done && hasEventListener(StorageEvent.CHANGE))
            {
                _changed = true;
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }

            return result;
        }

        public function removeSprite(id:uint):ChangeResult
        {
            if (id == 0 || id > _spritesCount)
            {
                throw new ArgumentError(Resources.getString("indexOutOfRange"));
            }

            var result:ChangeResult = internalRemoveSprite(id);
            _changed = _changed || result.done;
            if (result.done && hasEventListener(StorageEvent.CHANGE))
            {
                _changed = true;
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }

            return result;
        }

        public function removeSprites(sprites:Vector.<uint>):ChangeResult
        {
            if (!sprites)
            {
                throw new NullArgumentError("sprites");
            }

            var result:ChangeResult = internalRemoveSprites(sprites);
            _changed = _changed || result.done;
            if (result.done && hasEventListener(StorageEvent.CHANGE))
            {
                _changed = true;
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }

            return result;
        }

        public function getSprite(id:uint):Sprite
        {
            if (id == uint.MAX_VALUE)
                return _alertSprite;

            if (_loaded && id <= _spritesCount)
            {
                var sprite:Sprite;
                if (_sprites[id] !== undefined)
                    sprite = Sprite(_sprites[id]);
                else
                    sprite = readSprite(id);

                if (!sprite)
                {
                    sprite = _blankSprite;
                }

                return sprite;
            }
            return null;
        }

        public function getPixels(id:uint):ByteArray
        {
            if (_loaded)
            {
                var sprite:Sprite = getSprite(id);
                if (sprite)
                {
                    var pixels:ByteArray;
                    try
                    {
                        pixels = sprite.getPixels();
                    }
                    catch (error:Error)
                    {
                        Log.error(Resources.getString("failedToGetSprite", id), error.getStackTrace());
                        return _alertSprite.getPixels();
                    }
                    return pixels;
                }
            }
            return null;
        }

        /** Destructive imports must reject bad records, never import a preview error icon. */
        public function getPixelsStrict(id:uint):ByteArray
        {
            if (!_loaded || id > _spritesCount) throw new RangeError("Invalid source sprite id: " + id);
            if (id == 0) return _blankSprite.getPixels();
            var cached:Boolean = _sprites[id] !== undefined;
            var sprite:Sprite = cached ? Sprite(_sprites[id]) : _reader.readSprite(id);
            try { return sprite ? sprite.getPixels() : _blankSprite.getPixels(); }
            finally { if (sprite && !cached) sprite.dispose(); }
            return null;
        }

        public function getSpriteStrict(id:uint):Sprite
        {
            if (!_loaded || id > _spritesCount) throw new RangeError("Invalid source sprite id: " + id);
            if (id == 0) return _blankSprite;
            var cached:Boolean = _sprites[id] !== undefined;
            var sprite:Sprite = cached ? Sprite(_sprites[id]) : _reader.readSprite(id);
            if (sprite && cached) sprite.validateCompressedPixels();
            return sprite ? sprite : _blankSprite;
        }

        /**
         * Copies the pixels of a sprite to a bitmap.
         *
         * @param id The id of sprite.
         * @param bitmap The destination bitmap.
         * @param x The X destination point that represents the upper-left corner
         * of the SpriteExtent x SpriteExtent area where the new pixels are placed.
         * @param y The Y destination point that represents the upper-left corner of
         * the SpriteExtent x SpriteExtent area where the new pixels are placed.
         */
        public function copyPixels(id:int, bitmap:BitmapData, x:int, y:int):void
        {
            if (!this.loaded || !bitmap)
                return;

            try
            {
                var sprite:BitmapData = getBitmap(id, true);
                if (!sprite)
                    return;

                _point.x = x;
                _point.y = y;

                bitmap.copyPixels(sprite, _rect, _point, null, null, true);
            }
            catch (error:Error)
            {
                bitmap.copyPixels(SpriteUtils.createAlertBitmap(), _rect, _point, null, null, true);
                Log.error(Resources.getString("failedToGetSprite", id), error.getStackTrace());
            }
        }

        /**
         * @param backgroundColor A 32-bit ARGB color value.
         */
        public function getBitmap(id:uint, transparent:Boolean):BitmapData
        {
            if (!this.loaded || id == 0)
                return null;

            var sprite:Sprite = getSprite(id);
            if (!sprite)
                return null;

            var bitmap:BitmapData = sprite.getBitmap();
            if (!transparent)
                bitmap = SpriteUtils.fillBackground(bitmap);

            return bitmap;
        }

        /**
         * Calculates sprite hash for a ThingType like ItemEditor's ClientItem.SpriteHash.
         * Uses BGR0 format with 0x11 for transparent, vertically flipped.
         * @param thingType The thing to calculate hash for
         * @return 16-byte MD5 hash as ByteArray
         */
        public function getSpriteHash(thingType:ThingType):ByteArray
        {
            import otlib.animation.FrameGroup;
            import otlib.things.FrameGroupType;
            import flash.utils.Endian;
            import by.blooddy.crypto.MD5;

            var hash:ByteArray = new ByteArray();
            hash.length = 16;
            hash.position = 0;

            var group:FrameGroup = thingType.getFrameGroup(FrameGroupType.DEFAULT);
            if (!group)
                return hash;

            var layers:uint = group.layers;
            var height:uint = group.height;
            var width:uint = group.width;
            var spritesToHash:uint = width * height * layers;
            var spriteIndexList:Vector.<uint> = group.spriteIndex;

            if (!spriteIndexList || spriteIndexList.length < spritesToHash)
                return hash;

            var stream:ByteArray = new ByteArray();
            stream.endian = Endian.LITTLE_ENDIAN;

            const TRANSPARENT_COLOR:uint = 0x11;
            const SPRITE_SIZE:uint = 32;

            // Reusable buffer for getRGBData calls
            if (!_rgbDataBuffer)
            {
                _rgbDataBuffer = new ByteArray();
                _rgbDataBuffer.length = SpriteExtent.DEFAULT_SIZE * SpriteExtent.DEFAULT_SIZE * 3;
            }

            for (var i:uint = 0; i < spritesToHash; i++)
            {
                var spriteId:uint = spriteIndexList[i];
                var sprite:Sprite = getSprite(spriteId);

                if (!sprite || sprite.isEmpty)
                {
                    // Write transparent sprite
                    for (var ty:int = 0; ty < SPRITE_SIZE; ty++)
                    {
                        for (var tx:int = 0; tx < SPRITE_SIZE; tx++)
                        {
                            stream.writeByte(TRANSPARENT_COLOR);
                            stream.writeByte(TRANSPARENT_COLOR);
                            stream.writeByte(TRANSPARENT_COLOR);
                            stream.writeByte(0);
                        }
                    }
                    continue;
                }

                var rgbData:ByteArray = sprite.getRGBData(_rgbDataBuffer);

                // Flip vertically and convert RGB to BGR0
                for (var y:int = 0; y < SPRITE_SIZE; y++)
                {
                    for (var x:int = 0; x < SPRITE_SIZE; x++)
                    {
                        var srcY:int = SPRITE_SIZE - y - 1;
                        var srcPos:int = srcY * 96 + x * 3;

                        var r:uint = rgbData[srcPos + 0];
                        var g:uint = rgbData[srcPos + 1];
                        var b:uint = rgbData[srcPos + 2];

                        stream.writeByte(b);
                        stream.writeByte(g);
                        stream.writeByte(r);
                        stream.writeByte(0);
                    }
                }
            }

            stream.position = 0;
            var result:String = MD5.hashBytes(stream);

            hash.clear();
            for (var j:uint = 0; j < result.length; j += 2)
            {
                var hex:String = result.substr(j, 2);
                hash.writeByte(parseInt(hex, 16));
            }

            return hash;
        }

        public function hasSpriteId(id:uint):Boolean
        {
            if (_loaded && id <= _spritesCount)
            {
                return true;
            }
            return false;
        }

        /**
         * Checks if a sprite id and a sprite pixels are equal.
         *
         * @param id The id of the sprite to compare.
         * @param pixels Sprite pixels to compare.
         */
        public function compare(id:uint, pixels:ByteArray):Boolean
        {
            if (!pixels)
            {
                throw new NullArgumentError("pixels");
            }

            if (pixels.length != SpriteExtent.DEFAULT_DATA_SIZE)
            {
                throw new ArgumentError("Parameter pixels has an invalid length.");
            }

            if (hasSpriteId(id))
            {
                pixels.position = 0;
                var bmp1:BitmapData = new BitmapData(SpriteExtent.DEFAULT_SIZE, SpriteExtent.DEFAULT_SIZE, true, 0xFFFF00FF);
                bmp1.setPixels(bmp1.rect, pixels);
                var otherPixels:ByteArray = this.getPixels(id);
                otherPixels.position = 0;
                var bmp2:BitmapData = new BitmapData(SpriteExtent.DEFAULT_SIZE, SpriteExtent.DEFAULT_SIZE, true, 0xFFFF00FF);
                bmp2.setPixels(bmp2.rect, otherPixels);

                var difference:Object = bmp1.compare(bmp2);
                var result:Boolean = (difference === 0);
                if (difference is BitmapData) BitmapData(difference).dispose();

                // Dispose BitmapData to prevent memory leak
                bmp1.dispose();
                bmp2.dispose();

                return result;
            }
            return false;
        }

        // --------------------------------------
        // Private
        // --------------------------------------

        private function readSprite(id:uint):Sprite
        {
            try
            {
                return _reader.readSprite(id);
            }
            catch (error:Error)
            {
                Log.error(Resources.getString("failedToGetSprite", id), error.getStackTrace());
                return _alertSprite;
            }

            return null;
        }

        public function compile(file:File, version:Version, features:ClientFeatures):Boolean
        {
            if (!file)
            {
                throw new NullArgumentError("file");
            }

            if (!version)
            {
                throw new NullArgumentError("version");
            }

            if (!_loaded)
                return false;

            var compileFeatures:ClientFeatures = features.clone();
            compileFeatures.applyVersionDefaults(version.value);
            var equal:Boolean = FileUtil.equals(_file, file);
            var tmpFile:File;
            var done:Boolean = false;
            var readerClosed:Boolean = false;
            try
            {
                validateOutput(compileFeatures);
                if (file.name.toLowerCase() == "spr_index.dat")
                {
                    done = compileMulti(file, version, compileFeatures);
                    if (!_exporting && (equal || isTemporary)) onLoad(file, version, compileFeatures, true);
                }
                else
                {
                    tmpFile = file.parent.resolvePath(".ob-" + getTimer() + "-" + uint(Math.random() * uint.MAX_VALUE) + ".spr");
                    if (tmpFile.exists) throw new Error("SPR staging path already exists.");
                    writeSprFile(tmpFile, version, compileFeatures, 1, outputCount, 0);
                    if (equal && _reader) { _reader.closeSpriteFile(); readerClosed = true; }
                    // Do not delete the destination before a completed staging file exists.
                    var stagedPath:File = new File(tmpFile.nativePath);
                    tmpFile.moveTo(file, true);
                    if (stagedPath.exists || !file.exists) throw new Error("SPR output could not be replaced: " + file.nativePath);
                    if (!_exporting && (equal || isTemporary)) onLoad(file, version, compileFeatures, true);
                    done = true;
                }
            }
            catch (error:Error)
            {
                if (readerClosed && !done)
                {
                    try { _reader.openSpriteFile(file); } catch (reopenError:Error) { _loaded = false; }
                }
                dispatchEvent(new ErrorEvent(ErrorEvent.ERROR, false, false, error.getStackTrace(), error.errorID));
            }
            finally
            {
                if (tmpFile && tmpFile.exists) tmpFile.deleteFile();
            }
            if (done)
            {
                dispatchEvent(new StorageEvent(StorageEvent.COMPILE));
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }
            return done;
        }

        private var _outputSpriteSize:uint;
        private function get splitting():Boolean { return _outputSpriteSize == 32 && SpriteExtent.DEFAULT_SIZE == 64; }
        private function get outputCount():uint { return splitting ? _spritesCount * 4 : _spritesCount; }
        public function compileToStaging(file:File, version:Version, features:ClientFeatures, spriteSize:uint = 0):Boolean
        {
            _exporting = true;
            _outputSpriteSize = spriteSize;
            var result:Boolean;
            try { result = compile(file, version, features); }
            finally { _exporting = false; _outputSpriteSize = 0; }
            return result;
        }

        public function closeReader():void { if (_reader) _reader.closeSpriteFile(); }
        public function resumeReader():void { if (_reader && _file) _reader.openSpriteFile(_file); }
        public function bindCompiledFile(file:File, version:Version, features:ClientFeatures):void
        {
            onLoad(file, version, features, true);
        }

        public function validateOutput(features:ClientFeatures):void
        {
            if (splitting && _spritesCount > 0x3fffffff) throw new Error("Retiled sprite count exceeds 32 bits.");
            if (!features.extended && outputCount > 65535)
                throw new Error("16-bit SPR supports at most 65,535 sprites. Select 32-bit or multi-SPR; no sprites were discarded.");
        }

        private function writeSprFile(file:File, version:Version, features:ClientFeatures,
                first:uint, count:uint, completed:uint):void
        {
            var stream:FileStream = new FileStream();
            var addresses:ByteArray = new ByteArray(); addresses.endian = Endian.LITTLE_ENDIAN;
            var records:ByteArray = new ByteArray(); records.endian = Endian.LITTLE_ENDIAN;
            var lastProgress:int = getTimer();
            var split:Boolean = splitting;
            var all:uint = outputCount;
            var cachedId:uint, cachedPixels:ByteArray;
            reportProgress(completed, all);
            try
            {
                stream.open(file, FileMode.WRITE);
                stream.endian = Endian.LITTLE_ENDIAN;
                stream.writeUnsignedInt(version.sprSignature);
                if (features.extended) stream.writeUnsignedInt(count);
                else stream.writeShort(count);
                var header:uint = features.extended ? 8 : 6;
                var offset:Number = header + Number(count) * 4;
                if (offset > uint.MAX_VALUE) throw new Error("SPR address table exceeds the 4 GiB format limit.");
                stream.position = offset;
                for (var local:uint = 1; local <= count; local++)
                {
                    var id:uint = first + local - 1;
                    // Strict read: never compile an error-placeholder over damaged source data.
                    var borrowed:Boolean = _sprites[id] !== undefined;
                    var sprite:Sprite = split ? null : borrowed ? _sprites[id] : _reader.readSprite(id);
                    var fromReader:Sprite = borrowed ? null : sprite;
                    var converted:Sprite = null;
                    try
                    {
                        if (split)
                        {
                            var sourceId:uint = uint((id - 1) / 4) + 1;
                            if (cachedId != sourceId)
                            {
                                if (cachedPixels) cachedPixels.clear();
                                cachedPixels = getPixelsStrict(sourceId); cachedId = sourceId;
                            }
                            var piece:ByteArray = SpriteRetiler.split64(cachedPixels, (id - 1) % 4);
                            SpriteExtent.DEFAULT_SIZE = 32; SpriteExtent.DEFAULT_DATA_SIZE = 4096;
                            try { sprite = new Sprite(id, features.transparency); sprite.setPixels(piece); }
                            finally { SpriteExtent.DEFAULT_SIZE = 64; SpriteExtent.DEFAULT_DATA_SIZE = 16384; piece.clear(); }
                            fromReader = sprite;
                        }
                        if (sprite && !sprite.isEmpty && sprite.transparent != features.transparency)
                        {
                            converted = sprite.withTransparency(features.transparency);
                            sprite = converted;
                        }
                        if (!sprite || sprite.isEmpty) addresses.writeUnsignedInt(0);
                        else
                        {
                            if (sprite.length > 65535) throw new Error("SPR record length exceeds the 16-bit record-size limit.");
                            if (offset + 5 + sprite.length > uint.MAX_VALUE)
                                throw new Error("SPR exceeds 4 GiB. Use multi-SPR instead.");
                            addresses.writeUnsignedInt(uint(offset));
                            records.writeByte(255); records.writeByte(0); records.writeByte(255);
                            records.writeShort(sprite.length);
                            records.writeBytes(sprite.compressedPixels, 0, sprite.length);
                            offset += 5 + sprite.length;
                            if (records.length >= 4 * 1024 * 1024)
                            {
                                stream.writeBytes(records); records.clear();
                            }
                        }
                    }
                    finally { if (converted) converted.dispose(); if (fromReader) fromReader.dispose(); }
                    if (getTimer() - lastProgress >= 100 || local == count)
                    {
                        reportProgress(completed + local, all);
                        lastProgress = getTimer();
                    }
                }
                if (records.length) stream.writeBytes(records);
                // Sparse files still need the complete address table when every sprite is empty.
                stream.position = header;
                if (addresses.length) stream.writeBytes(addresses);
            }
            finally { try { stream.close(); } catch (error:Error) {} addresses.clear(); records.clear(); if (cachedPixels) cachedPixels.clear(); }
        }

        private function reportProgress(value:uint, total:uint):void
        {
            if (hasEventListener(ProgressEvent.PROGRESS))
                dispatchEvent(new ProgressEvent(ProgressEvent.PROGRESS, ProgressBarID.SPRITES, value, total, "Saving SPR"));
        }

        private function compileMulti(file:File, version:Version, features:ClientFeatures):Boolean
        {
            var root:File = file.parent;
            var stage:File = AssetOutputTransaction.createStage(root);
            var suspended:Boolean = false;
            var success:Boolean = false;
            try
            {
                var partsDir:File = stage.resolvePath("FileParts");
                partsDir.createDirectory();
                var parts:ByteArray = new ByteArray(); parts.endian = Endian.LITTLE_ENDIAN;
                var index:ByteArray = new ByteArray(); index.endian = Endian.LITTLE_ENDIAN;
                var num:uint = Math.ceil(outputCount / 65535);
                if (!num || num > 10000) throw new Error("Unsupported multi-SPR part count.");
                parts.writeUTFBytes("SPMT"); parts.writeUnsignedInt(num);
                index.writeUTFBytes("SPIX"); index.writeUnsignedInt(1); index.writeUnsignedInt(outputCount);
                var completed:uint = 0;
                for (var p:uint = 1; p <= num; p++)
                {
                    var n:uint = Math.min(65535, outputCount - completed);
                    writeSprFile(partsDir.resolvePath("part_" + p + ".spr"), version, features, completed + 1, n, completed);
                    parts.writeUnsignedInt(version.sprSignature); parts.writeUnsignedInt(n);
                    for (var id:uint = 1; id <= n; id++) index.writeUnsignedInt((p << 16) | id);
                    completed += n;
                }
                writeBytesFile(stage.resolvePath("spr_parts.dat"), parts);
                writeBytesFile(stage.resolvePath("spr_index.dat"), index);
                if (FileUtil.equals(_file, file)) { closeReader(); suspended = true; }
                AssetOutputTransaction.publish(stage, root, ["FileParts", "spr_parts.dat", "spr_index.dat"]);
                success = true;
            }
            finally
            {
                if (suspended) resumeReader();
                // Retain unpublished artifacts if publication/rollback failed.
                if (success && stage.exists) stage.deleteDirectory(true);
            }
            return true;
        }

        private static function writeBytesFile(file:File, bytes:ByteArray):void
        {
            var stream:FileStream = new FileStream();
            try { stream.open(file, FileMode.WRITE); stream.writeBytes(bytes); }
            finally { try { stream.close(); } catch (error:Error) {} }
        }

        public function isEmptySprite(id:uint):Boolean
        {
            if (_loaded && id <= _spritesCount)
            {
                if (_sprites[id] !== undefined)
                    return Sprite(_sprites[id]).isEmpty;
                else
                    return _reader.isEmptySprite(id);
            }
            return true;
        }

        public function unload():void
        {
            var event:StorageEvent = new StorageEvent(StorageEvent.UNLOADING, false, true);
            dispatchEvent(event);

            if (event.isDefaultPrevented())
                return;

            if (_reader)
            {
                _reader.closeSpriteFile();
                _reader = null;
            }

            _file = null;
            _loaded = false;
            _signature = 0;
            _currentFeatures = null;
            _version = null;
            _sprites = null;
            _spritesCount = 0;
            _blankSprite = null;
            _alertSprite = null;
            _headerSize = 0;
            if (_rgbDataBuffer) _rgbDataBuffer.clear();
            _rgbDataBuffer = null;
            _changed = false;

            dispatchEvent(new StorageEvent(StorageEvent.UNLOAD));
            dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
        }

        public function invalidate():void
        {
            if (!_changed)
            {
                _changed = true;

                if (hasEventListener(StorageEvent.CHANGE))
                    dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }
        }

        // --------------------------------------
        // Internal
        // --------------------------------------

        otlib_internal function internalAddSprite(pixels:ByteArray, result:ChangeResult = null):ChangeResult
        {
            result = result ? result : new ChangeResult();

            if (pixels.length != SpriteExtent.DEFAULT_DATA_SIZE)
            {
                return result.update(null, false, "Parameter pixels has an invalid length.");
            }

            if (this.isFull)
            {
                return result.update(null, false, Resources.getString("spritesLimitReached"));
            }

            var id:uint = _spritesCount + 1;
            var sprite:Sprite = new Sprite(id, _currentFeatures ? _currentFeatures.transparency : false);
            if (!sprite.setPixels(pixels))
            {
                var message:String = Resources.getString(
                        "failedToAdd",
                        Resources.getString("sprite"),
                        id);
                return result.update(null, false, message);
            }

            // Add sprite to list.
            _sprites[id] = sprite;
            _spritesCount = id;

            // Returns the pixels buffer position.
            pixels.position = 0;

            var data:SpriteData = SpriteData.createSpriteData(id, sprite.getPixels());
            return result.update([data], true);
        }

        otlib_internal function internalAddSprites(sprites:Vector.<ByteArray>, result:ChangeResult = null):ChangeResult
        {
            result = result ? result : new ChangeResult();

            var addedList:Array = [];
            var length:uint = sprites.length;
            for (var i:uint = 0; i < length; i++)
            {
                var added:ChangeResult = internalAddSprite(sprites[i], CHANGE_RESULT_HELPER);
                if (!added.done)
                {
                    return result.update(addedList, false, added.message);
                }
                addedList[i] = added.list[0];
            }
            return result.update(addedList, true);
        }

        otlib_internal function internalReplaceSprite(id:uint, pixels:ByteArray, result:ChangeResult = null):ChangeResult
        {
            result = result ? result : new ChangeResult();

            if (id == 0)
                return result.update(null, true);

            var sprite:Sprite = new Sprite(id, _currentFeatures ? _currentFeatures.transparency : false);
            if (!sprite.setPixels(pixels))
            {
                var message:String = Resources.getString(
                        "failedToReplace",
                        Resources.getString("sprite"),
                        id);
                return result.update(null, false, message);
            }

            // Get the removed sprite.
            var removed:Sprite = getSprite(id);

            // Add sprite to list.
            _sprites[id] = sprite;

            // Return the ByteArray position.
            pixels.position = 0;
            var data:SpriteData = SpriteData.createSpriteData(id, removed.getPixels());
            return result.update([data], true);
        }

        otlib_internal function internalReplaceSprites(sprites:Vector.<SpriteData>, result:ChangeResult = null):ChangeResult
        {
            result = result ? result : new ChangeResult();

            var replacedList:Array = [];
            var length:uint = sprites.length;

            for (var i:uint = 0; i < length; i++)
            {
                var id:uint = sprites[i].id;
                var pixels:ByteArray = sprites[i].pixels;
                var replaced:ChangeResult = internalReplaceSprite(id, pixels, CHANGE_RESULT_HELPER);
                if (!replaced.done)
                {
                    return result.update(replacedList, false, replaced.message);
                }

                if (replaced.list)
                    replacedList[i] = replaced.list[0];
            }
            return result.update(replacedList, true);
        }

        otlib_internal function internalRemoveSprite(id:uint, result:ChangeResult = null):ChangeResult
        {
            result = result ? result : new ChangeResult();

            // Get the removed sprite.
            var removed:Sprite = getSprite(id);

            if (id == _spritesCount && id != 1)
            {
                delete _sprites[id];
                _spritesCount--;
            }
            else
            {
                // Add a blank sprite at index.
                _sprites[id] = new Sprite(id, _currentFeatures ? _currentFeatures.transparency : false);
            }

            var data:SpriteData = SpriteData.createSpriteData(id, removed.getPixels());
            return result.update([data], true);
        }

        otlib_internal function internalRemoveSprites(sprites:Vector.<uint>, result:ChangeResult = null):ChangeResult
        {
            result = result ? result : new ChangeResult();

            var removedList:Array = [];
            var length:uint = sprites.length;

            // Removes last sprite first
            sprites.sort(Array.NUMERIC | Array.DESCENDING);

            for (var i:uint = 0; i < length; i++)
            {
                var id:uint = sprites[i];
                if (id != 0 && hasSpriteId(id))
                {
                    var removed:ChangeResult = internalRemoveSprite(id, CHANGE_RESULT_HELPER);
                    if (!removed.done)
                    {
                        return result.update(removedList, false, removed.message);
                    }
                    removedList[removedList.length] = removed.list[0];
                }
            }
            return result.update(removedList, true);
        }

        // --------------------------------------
        // Private
        // --------------------------------------

        private function onLoad(file:File,
                version:Version,
                features:ClientFeatures,
                reloading:Boolean):void
        {
            if (!file.exists)
            {
                Log.error(Resources.getString("fileNotFound", file.nativePath));
                return;
            }

            _file = file;
            _version = version;
            _currentFeatures = features.clone();
            _currentFeatures.applyVersionDefaults(version.value);
            if (_reader) _reader.closeSpriteFile();
            _reader = new SpriteReader(_currentFeatures);
            _reader.openSpriteFile(file, reloading ? null : loadProgress);
            _signature = _reader.readSignature();
            _spritesCount = _reader.readSpriteCount();
            _headerSize = _currentFeatures.extended ? SpriteFileSize.HEADER_U32 : SpriteFileSize.HEADER_U16;
            _blankSprite = new Sprite(0, _currentFeatures.transparency);
            _alertSprite = createAlertSprite(_currentFeatures.transparency);
            _sprites = new Dictionary();
            _sprites[0] = _blankSprite;
            _changed = false;
            _loaded = true;

            if (!reloading)
            {
                dispatchEvent(new StorageEvent(StorageEvent.LOAD));
                dispatchEvent(new StorageEvent(StorageEvent.CHANGE));
            }
        }

        private function createAlertSprite(transparent:Boolean):Sprite
        {
            var bitmap:BitmapData = SpriteUtils.createAlertBitmap();
            var sprite:Sprite = new Sprite(uint.MAX_VALUE, transparent);
            sprite.setPixels(bitmap.getPixels(bitmap.rect));
            // SpriteUtils owns the shared alert bitmap; do not dispose it here.
            return sprite;
        }

        private function loadProgress(done:uint, total:uint, label:String):void
        {
            if (hasEventListener(ProgressEvent.PROGRESS))
                dispatchEvent(new ProgressEvent(ProgressEvent.PROGRESS, ProgressBarID.SPRITES, done, total, label));
        }

        // --------------------------------------------------------------------------
        // STATIC
        // --------------------------------------------------------------------------

        private static const CHANGE_RESULT_HELPER:ChangeResult = new ChangeResult();
    }
}
