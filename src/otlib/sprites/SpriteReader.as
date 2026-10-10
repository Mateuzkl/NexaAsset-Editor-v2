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
    import flash.filesystem.FileStream;
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.utils.Endian;
    import otlib.core.ClientFeatures;

    public class SpriteReader extends FileStream implements ISpriteReader
    {
        // --------------------------------------------------------------------------
        // PROPERTIES
        // --------------------------------------------------------------------------

        private var m_extended:Boolean;
        private var m_transparency:Boolean;
        private var m_headerSize:uint;
        private var m_fileSize:Number;
        private var m_count:uint;
        private var m_multi:MultiSpriteReader;

        // --------------------------------------------------------------------------
        // CONSTRUCTOR
        // --------------------------------------------------------------------------

        public function SpriteReader(features:ClientFeatures)
        {
            m_extended = features ? features.extended : false;
            m_transparency = features ? features.transparency : false;
            m_headerSize = m_extended ? SpriteFileSize.HEADER_U32 : SpriteFileSize.HEADER_U16;

            endian = Endian.LITTLE_ENDIAN;
        }

        // --------------------------------------------------------------------------
        // METHODS
        // --------------------------------------------------------------------------

        // --------------------------------------
        // Public
        // --------------------------------------

        public function openSpriteFile(file:File):void
        {
            if (file.name.toLowerCase() == "spr_index.dat")
            {
                m_multi = new MultiSpriteReader(m_transparency);
                m_multi.open(file);
                return;
            }
            try
            {
                open(file, FileMode.READ);
                m_fileSize = file.size;
                if (m_fileSize < m_headerSize)
                    throw new Error("Truncated SPR header: " + file.nativePath);
                m_count = readSpriteCount();
                if (m_headerSize + Number(m_count) * 4 > m_fileSize)
                    throw new Error("SPR address table exceeds file size: " + file.nativePath);
            }
            catch (error:Error)
            {
                closeSpriteFile();
                throw error;
            }
        }

        public function closeSpriteFile():void
        {
            if (m_multi) { m_multi.close(); m_multi = null; }
            else { try { close(); } catch (error:Error) {} }
            m_count = 0;
            m_fileSize = 0;
        }

        public function readSignature():uint
        {
            if (m_multi) return m_multi.signature;
            position = SpriteFilePosition.SIGNATURE;
            return readUnsignedInt();
        }

        public function readSpriteCount():uint
        {
            if (m_multi) return m_multi.count;
            position = SpriteFilePosition.LENGTH;
            return m_extended ? readUnsignedInt() : readUnsignedShort();
        }

        public function readSprite(id:uint):Sprite
        {
            if (m_multi) return m_multi.readSprite(id);
            if (id == 0 || id > m_count) throw new RangeError("Invalid SPR id: " + id);
            position = ((id - 1) * SpriteFileSize.ADDRESS) + m_headerSize;

            var address:uint = readUnsignedInt();
            if (address == 0)
                return null;

            if (address < m_headerSize + Number(m_count) * 4 || Number(address) + 5 > m_fileSize)
                throw new Error("Invalid SPR offset for sprite " + id);

            position = address;
            readUnsignedByte(); // skip red color
            readUnsignedByte(); // skip green color
            readUnsignedByte(); // skip blue color

            var sprite:Sprite = new Sprite(id, m_transparency);
            var length:uint = readUnsignedShort();
            if (Number(address) + 5 + length > m_fileSize)
                throw new Error("Truncated SPR payload for sprite " + id);

            if (length != 0)
            {
                readBytes(sprite.compressedPixels, 0, length);
            }
            sprite.validateCompressedPixels();

            return sprite;
        }

        public function isEmptySprite(id:uint):Boolean
        {
            var sprite:Sprite = readSprite(id);
            return !sprite || sprite.isEmpty;
        }
    }
}
