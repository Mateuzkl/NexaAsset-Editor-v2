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
    import flash.utils.ByteArray;
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
        private var m_offsets:ByteArray;
        private var m_window:ByteArray;
        private var m_windowStart:Number = -1;
        public var transparencyKnown:Boolean;
        public var transparencyConflict:Boolean;

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

        public function openSpriteFile(file:File, progress:Function = null):void
        {
            closeSpriteFile();
            try
            {
                if (file.name.toLowerCase() == "spr_index.dat")
                {
                    m_multi = new MultiSpriteReader(m_transparency);
                    m_multi.open(file, progress);
                    return;
                }
                open(file, FileMode.READ);
                m_fileSize = file.size;
                if (m_fileSize < m_headerSize)
                    throw new Error("Truncated SPR header: " + file.nativePath);
                m_count = readSpriteCount();
                if (m_headerSize + Number(m_count) * 4 > m_fileSize)
                    throw new Error("SPR address table exceeds file size: " + file.nativePath);
                if (Number(m_count) * 4 > 0x7FFFFFFF) throw new Error("SPR address table is too large.");
                m_offsets = new ByteArray(); m_offsets.endian = Endian.LITTLE_ENDIAN;
                position = m_headerSize;
                if (m_count) readBytes(m_offsets, 0, m_count * 4);
                m_window = new ByteArray(); m_window.endian = Endian.LITTLE_ENDIAN;
                if (progress != null) progress(m_count, m_count, "Loading SPR address table");
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
            try { close(); } catch (error:Error) {}
            m_count = 0;
            m_fileSize = 0;
            if (m_offsets) m_offsets.clear();
            if (m_window) m_window.clear();
            m_offsets = null; m_window = null; m_windowStart = -1;
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
            var sprite:Sprite = readSpriteRecord(id);
            if (sprite) sprite.validateCompressedPixels();
            return sprite;
        }

        internal function readSpriteRecord(id:uint):Sprite
        {
            if (m_multi) return m_multi.readSpriteRecord(id);
            if (id == 0 || id > m_count) throw new RangeError("Invalid SPR id: " + id);
            m_offsets.position = Number(id - 1) * 4;

            var address:uint = m_offsets.readUnsignedInt();
            if (address == 0)
                return null;

            if (address < m_headerSize + Number(m_count) * 4 || Number(address) + 5 > m_fileSize)
                throw new Error("Invalid SPR offset for sprite " + id);

            ensureWindow(address, 5);
            m_window.position = address - m_windowStart + 3;

            var sprite:Sprite = new Sprite(id, m_transparency);
            var length:uint = m_window.readUnsignedShort();
            if (Number(address) + 5 + length > m_fileSize)
                throw new Error("Truncated SPR payload for sprite " + id);

            if (length != 0)
            {
                ensureWindow(address, 5 + length);
                sprite.compressedPixels.writeBytes(m_window, address - m_windowStart + 5, length);
            }

            return sprite;
        }

        private function ensureWindow(address:Number, length:uint):void
        {
            if (m_windowStart >= 0 && address >= m_windowStart && address + length <= m_windowStart + m_window.length) return;
            m_window.clear();
            m_windowStart = address;
            position = address;
            readBytes(m_window, 0, Math.min(1024 * 1024, m_fileSize - address));
        }

        public function detectTransparency():Boolean
        {
            var preferred:uint = 0, opposite:uint = 0;
            transparencyKnown = false;
            transparencyConflict = false;
            var count:uint = readSpriteCount();
            // Include distant records: some large archives begin with empty sprites.
            // Sampling stays bounded and never scans/decompresses the whole archive.
            var prefix:uint = Math.min(count, 4096);
            var earlySamples:uint = 0, lastEarlyID:uint = 0, lastSpreadID:uint = 0;
            for (var step:uint = 1; step <= prefix + 64 && count; step++)
            {
                var id:uint;
                if (step <= prefix)
                {
                    if (earlySamples >= 16) continue;
                    id = step; lastEarlyID = id;
                }
                else
                {
                    id = 1 + Math.floor(Number(count - 1) * (step - prefix) / 64);
                    if (id <= lastEarlyID || id == lastSpreadID) continue;
                    lastSpreadID = id;
                }
                var sprite:Sprite = readSpriteRecord(id);
                if (!sprite) continue;
                if (sprite.isEmpty) { sprite.dispose(); continue; }
                if (step <= prefix) earlySamples++;
                var other:Sprite = new Sprite(id, !m_transparency);
                other.compressedPixels.writeBytes(sprite.compressedPixels);
                var firstValid:Boolean = validRle(sprite), secondValid:Boolean = validRle(other);
                if (firstValid && !secondValid) preferred++;
                if (!firstValid && secondValid) opposite++;
                sprite.dispose(); other.dispose();
            }
            if (!preferred && opposite >= 3)
            {
                m_transparency = !m_transparency;
                if (m_multi) m_multi.setTransparency(m_transparency);
            }
            transparencyConflict = preferred > 0 && opposite > 0;
            transparencyKnown = !transparencyConflict && (preferred >= 3 || opposite >= 3);
            return m_transparency;
        }

        /** A bounded structural probe. Ambiguous headers retain the caller's hint. */
        public static function probeHeader(file:File, preferred:Boolean):Object
        {
            if (file.name.toLowerCase() == "spr_index.dat") return {extended:true, known:true};
            var shortValid:Boolean = validHeader(file, false);
            var longValid:Boolean = validHeader(file, true);
            if (!shortValid && !longValid) throw new Error("Invalid SPR header/address table.");
            return {extended:shortValid == longValid ? preferred : longValid, known:shortValid != longValid};
        }

        private static function validHeader(file:File, extended:Boolean):Boolean
        {
            var stream:FileStream = new FileStream();
            try
            {
                stream.open(file, FileMode.READ); stream.endian = Endian.LITTLE_ENDIAN;
                var header:uint = extended ? 8 : 6;
                if (file.size < header) return false;
                stream.position = 4;
                var count:uint = extended ? stream.readUnsignedInt() : stream.readUnsignedShort();
                var end:Number = header + Number(count) * 4;
                if (end > file.size || (!count && file.size != header)) return false;
                for (var i:uint = 0; i < Math.min(count, 64); i++)
                {
                    var index:uint = count <= 64 || i < 16 ? i : Math.floor(Number(count - 1) * i / 63);
                    stream.position = header + Number(index) * 4;
                    var address:uint = stream.readUnsignedInt();
                    if (!address) continue;
                    if (address < end || Number(address) + 5 > file.size) return false;
                    stream.position = Number(address) + 3;
                    if (Number(address) + 5 + stream.readUnsignedShort() > file.size) return false;
                }
                return true;
            }
            catch (error:Error) { return false; }
            finally { try { stream.close(); } catch (closeError:Error) {} }
            return false;
        }

        private static function validRle(sprite:Sprite):Boolean
        {
            try { sprite.validateCompressedPixels(); return true; }
            catch (error:Error) { return false; }
            return false;
        }

        public function isEmptySprite(id:uint):Boolean
        {
            var sprite:Sprite = readSprite(id);
            return !sprite || sprite.isEmpty;
        }
    }
}
