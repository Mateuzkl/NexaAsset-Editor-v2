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
    import flash.geom.Rectangle;
    import flash.utils.ByteArray;
    import flash.utils.Endian;

    import by.blooddy.crypto.MD5;

    import nail.errors.NullArgumentError;
    import otlib.utils.SpriteExtent;

    /**
     * The Sprite class represents an image with SpriteExtent x SpriteExtent pixels.
     */
    public class Sprite
    {
        // --------------------------------------------------------------------------
        // PROPERTIES
        // --------------------------------------------------------------------------

        private var _id:uint;
        private var _transparent:Boolean;
        private var _compressedPixels:ByteArray;
        private var _bitmap:BitmapData;
        private var _hash:String;
        private var _rect:Rectangle;

        private static const TRANSPARENT_COLOR:uint = 0x11;
        private static const RGB_SIZE:uint = 3072; // 32 * 32 * 3

        // --------------------------------------
        // Getters / Setters
        // --------------------------------------

        /** The id of the sprite. This value specifies the index in the spr file. **/
        public function get id():uint
        {
            return _id;
        }
        public function set id(value:uint):void
        {
            _id = value;
        }

        /** Specifies whether the sprite supports per-pixel transparency. **/
        public function get transparent():Boolean
        {
            return _transparent;
        }
        public function set transparent(value:Boolean):void
        {
            if (_transparent != value)
            {

                var pixels:ByteArray = getPixels();
                _transparent = value;
                setPixels(pixels);
            }
        }

        /** Indicates if the sprite does not have colored pixels. **/
        public function get isEmpty():Boolean
        {
            return (_compressedPixels.length == 0);
        }

        internal function get length():uint
        {
            return _compressedPixels.length;
        }
        internal function get compressedPixels():ByteArray
        {
            return _compressedPixels;
        }

        // --------------------------------------------------------------------------
        // CONSTRUCTOR
        // --------------------------------------------------------------------------

        public function Sprite(id:uint, transparent:Boolean)
        {
            _id = id;
            _transparent = transparent;
            _compressedPixels = new ByteArray();
            _compressedPixels.endian = Endian.LITTLE_ENDIAN;

            _rect = new Rectangle(0, 0, SpriteExtent.DEFAULT_SIZE, SpriteExtent.DEFAULT_SIZE);
        }

        // --------------------------------------------------------------------------
        // METHODS
        // --------------------------------------------------------------------------

        // --------------------------------------
        // Public
        // --------------------------------------

        /**
         * Returns the <code>id</code> string representation of the <code>Sprite</code>.
         */
        public function toString():String
        {
            return _id.toString();
        }

        public function getPixels(target:ByteArray = null):ByteArray
        {
            return uncompressPixels(target);
        }

        internal function validateCompressedPixels():void
        {
            _compressedPixels.position = 0;
            var pixels:Number = 0;
            var channels:uint = _transparent ? 4 : 3;
            while (_compressedPixels.bytesAvailable)
            {
                if (_compressedPixels.bytesAvailable < 4) throw new Error("Truncated SPR RLE header.");
                pixels += _compressedPixels.readUnsignedShort();
                var colored:uint = _compressedPixels.readUnsignedShort();
                pixels += colored;
                var bytes:Number = Number(colored) * channels;
                if (pixels > SpriteExtent.DEFAULT_DATA_SIZE / 4 || bytes > _compressedPixels.bytesAvailable)
                    throw new Error("Invalid SPR RLE run length for sprite " + _id +
                        " (pixels=" + pixels + ", colored=" + colored + ", channels=" + channels +
                        ", remaining=" + _compressedPixels.bytesAvailable + ").");
                _compressedPixels.position += bytes;
            }
            _compressedPixels.position = 0;
        }

        /** Convert only colored RLE runs; do not inflate/recompress 1,024 pixels. */
        internal function withTransparency(value:Boolean):Sprite
        {
            validateCompressedPixels();
            var result:Sprite = new Sprite(_id, value);
            // Reclassify all alpha-zero pixels as empty runs when removing alpha;
            // RGB -> RGBA can keep all RLE runs verbatim.
            if (_transparent && !value)
            {
                result.setPixels(getPixels());
                return result;
            }
            var source:ByteArray = _compressedPixels, target:ByteArray = result.compressedPixels;
            source.position = 0;
            while (source.bytesAvailable)
            {
                target.writeShort(source.readUnsignedShort());
                var colored:uint = source.readUnsignedShort();
                target.writeShort(colored);
                if (value == _transparent)
                {
                    var bytes:uint = colored * (value ? 4 : 3);
                    if (bytes) target.writeBytes(source, source.position, bytes);
                    source.position += bytes;
                }
                else for (var i:uint = 0; i < colored; i++)
                {
                    // Both buffers use little endian: emit R,G,B,255 in one write.
                    var rgba:uint = source.readUnsignedByte() | (source.readUnsignedByte() << 8) |
                        (source.readUnsignedByte() << 16) | 0xFF000000;
                    target.writeUnsignedInt(rgba);
                }
            }
            source.position = 0;
            return result;
        }

        /**
         * Gets RGB data like ItemEditor's GetRGBData().
         * Returns 3072 bytes (32*32*3), with 0x11 for transparent pixels.
         * @param target Optional reusable ByteArray buffer
         */
        public function getRGBData(target:ByteArray = null):ByteArray
        {
            var rgb:ByteArray = target ? target : new ByteArray();
            rgb.length = RGB_SIZE;
            rgb.position = 0;

            if (isEmpty)
            {
                for (var k:uint = 0; k < RGB_SIZE; k++)
                {
                    rgb[k] = TRANSPARENT_COLOR;
                }
                return rgb;
            }

            _compressedPixels.position = 0;
            var write:uint = 0;
            var length:uint = _compressedPixels.length;
            var bitPerPixel:uint = _transparent ? 4 : 3;
            var transparentPixels:uint;
            var coloredPixels:uint;
            var read:uint = 0;

            while (read < length)
            {
                // Read chunks (2 bytes transparent count, 2 bytes colored count)
                // Note: compressedPixels contains: [transparent_count (2)] [colored_count (2)] [colored_data...]
                // We need to read carefully from _compressedPixels.

                // _compressedPixels is Little Endian.
                transparentPixels = _compressedPixels.readUnsignedShort();
                coloredPixels = _compressedPixels.readUnsignedShort();

                // Advance read counter (headers + data)
                if (write + Number(transparentPixels + coloredPixels) * 3 > RGB_SIZE ||
                        _compressedPixels.bytesAvailable < Number(coloredPixels) * bitPerPixel)
                    throw new Error("Invalid SPR RLE run length.");
                read += 4 + (coloredPixels * bitPerPixel);

                // Write transparent pixels (filled with 0x11)
                for (var i:int = 0; i < transparentPixels; i++)
                {
                    rgb[write++] = TRANSPARENT_COLOR;
                    rgb[write++] = TRANSPARENT_COLOR;
                    rgb[write++] = TRANSPARENT_COLOR;
                }

                // Write colored pixels
                for (var j:int = 0; j < coloredPixels; j++)
                {
                    var r:uint = _compressedPixels.readUnsignedByte();
                    var g:uint = _compressedPixels.readUnsignedByte();
                    var b:uint = _compressedPixels.readUnsignedByte();

                    if (_transparent)
                    {
                        // Skip Alpha byte, do NOT use it to override color with 0x11
                        _compressedPixels.readUnsignedByte();
                    }

                    rgb[write++] = r;
                    rgb[write++] = g;
                    rgb[write++] = b;
                }
            }

            // Fill remaining pixels with transparent color
            while (write < RGB_SIZE)
            {
                rgb[write++] = TRANSPARENT_COLOR;
                rgb[write++] = TRANSPARENT_COLOR;
                rgb[write++] = TRANSPARENT_COLOR;
            }

            return rgb;
        }

        public function setPixels(pixels:ByteArray):Boolean
        {
            if (!pixels)
                throw new NullArgumentError("pixels");

            if (pixels.length != SpriteExtent.DEFAULT_DATA_SIZE)
                throw new Error("Invalid sprite pixels length");

            _hash = null;
            if (_bitmap) { _bitmap.dispose(); _bitmap = null; }
            return compressPixels(pixels);
        }

        public function getBitmap():BitmapData
        {
            if (_bitmap)
                return _bitmap;

            var pixels:ByteArray = getPixels();
            if (!pixels)
                return null;

            _bitmap = new BitmapData(SpriteExtent.DEFAULT_SIZE, SpriteExtent.DEFAULT_SIZE, true);
            _bitmap.setPixels(_rect, pixels);

            return _bitmap;
        }

        public function setBitmap(bitmap:BitmapData):Boolean
        {
            if (!bitmap)
                throw new NullArgumentError("bitmap");

            if (bitmap.width != SpriteExtent.DEFAULT_SIZE || bitmap.height != SpriteExtent.DEFAULT_SIZE)
                throw new Error("Invalid sprite bitmap size");

            if (!compressPixels(bitmap.getPixels(_rect)))
                return false;

            _hash = null;
            var copy:BitmapData = bitmap.clone();
            if (_bitmap) _bitmap.dispose();
            _bitmap = copy;
            return true;
        }

        public function getHash():String
        {
            if (_hash != null)
                return _hash;

            if (_compressedPixels.length != 0)
                _hash = MD5.hashBytes(_compressedPixels);

            return _hash;
        }

        public function clone():Sprite
        {
            var sprite:Sprite = new Sprite(_id, _transparent);

            _compressedPixels.position = 0;
            _compressedPixels.readBytes(sprite._compressedPixels);

            sprite._bitmap = _bitmap ? _bitmap.clone() : null;
            return sprite;
        }

        public function clear():void
        {
            if (_compressedPixels)
                _compressedPixels.clear();

            if (_bitmap)
                _bitmap.fillRect(_rect, 0x00FF00FF);
            _hash = null;
        }

        public function dispose():void
        {
            if (_compressedPixels)
                _compressedPixels.clear();

            if (_bitmap)
            {
                _bitmap.dispose();
                _bitmap = null;
            }

            _id = 0;
        }

        // --------------------------------------
        // Private
        // --------------------------------------

        private function compressPixels(pixels:ByteArray):Boolean
        {
            _compressedPixels.clear();
            pixels.position = 0;

            var index:uint;
            var color:uint;
            var transparentPixel:Boolean = true;
            var alphaCount:uint;
            var chunkSize:uint;
            var coloredPos:uint;
            var finishOffset:uint;
            var length:uint = pixels.length / 4;

            while (index < length)
            {

                chunkSize = 0;
                while (index < length)
                {
                    pixels.position = index * 4;
                    color = pixels.readUnsignedInt();
                    // ARGB alpha defines visibility. Modern sheets may retain
                    // magenta (or any RGB) underneath a fully transparent pixel.
                    transparentPixel = (color >>> 24) == 0;
                    if (!transparentPixel)
                        break;
                    alphaCount++;
                    chunkSize++;
                    index++;
                }

                // Entire image is transparent
                if (alphaCount < length)
                {
                    // Already at the end
                    if (index < length)
                    {
                        _compressedPixels.writeShort(chunkSize); // Write transparent pixels
                        coloredPos = _compressedPixels.position; // Save colored position
                        _compressedPixels.position += 2; // Skip colored short
                        chunkSize = 0;

                        while (index < length)
                        {
                            pixels.position = index * 4;
                            color = pixels.readUnsignedInt();
                            transparentPixel = (color >>> 24) == 0;
                            if (transparentPixel)
                                break;

                            _compressedPixels.writeByte(color >> 16 & 0xFF); // Write red
                            _compressedPixels.writeByte(color >> 8 & 0xFF); // Write green
                            _compressedPixels.writeByte(color & 0xFF); // Write blue
                            if (_transparent)
                                _compressedPixels.writeByte(color >> 24 & 0xFF); // Write Alpha

                            chunkSize++;
                            index++;
                        }

                        finishOffset = _compressedPixels.position;
                        _compressedPixels.position = coloredPos; // Go back to chunksize indicator
                        _compressedPixels.writeShort(chunkSize); // Write colored pixels
                        _compressedPixels.position = finishOffset;
                    }
                }
            }

            return true;
        }

        private function uncompressPixels(target:ByteArray = null):ByteArray
        {
            var read:uint;
            var write:uint;
            var transparentPixels:uint;
            var coloredPixels:uint;
            var alpha:uint;
            var red:uint;
            var green:uint;
            var blue:uint;
            var channels:uint = _transparent ? 4 : 3;
            var length:uint = _compressedPixels.length;
            var i:int;

            _compressedPixels.position = 0;
            var pixels:ByteArray = target ? target : new ByteArray();
            pixels.length = SpriteExtent.DEFAULT_DATA_SIZE;
            pixels.position = 0;
            write = 0;

            for (read = 0; read < length; read += 4 + (channels * coloredPixels))
            {

                transparentPixels = _compressedPixels.readUnsignedShort();
                coloredPixels = _compressedPixels.readUnsignedShort();

                if (write + Number(transparentPixels + coloredPixels) * 4 > SpriteExtent.DEFAULT_DATA_SIZE ||
                        _compressedPixels.bytesAvailable < Number(coloredPixels) * channels)
                    throw new Error("Invalid SPR RLE run length.");
                for (i = 0; i < transparentPixels; i++)
                {
                    pixels[write++] = 0x00; // Alpha
                    pixels[write++] = 0x00; // Red
                    pixels[write++] = 0x00; // Green
                    pixels[write++] = 0x00; // Blue
                }

                for (i = 0; i < coloredPixels; i++)
                {
                    red = _compressedPixels.readUnsignedByte(); // Red
                    green = _compressedPixels.readUnsignedByte(); // Green
                    blue = _compressedPixels.readUnsignedByte(); // Blue
                    alpha = _transparent ? _compressedPixels.readUnsignedByte() : 0xFF; // Alpha

                    pixels[write++] = alpha; // Alpha
                    pixels[write++] = red; // Red
                    pixels[write++] = green; // Green
                    pixels[write++] = blue; // Blue
                }
            }

            while (write < SpriteExtent.DEFAULT_DATA_SIZE)
            {
                pixels[write++] = 0x00; // Alpha
                pixels[write++] = 0x00; // Red
                pixels[write++] = 0x00; // Green
                pixels[write++] = 0x00; // Blue
            }

            return pixels;
        }
    }
}
