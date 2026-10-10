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

package otlib.utils
{
    import flash.events.ErrorEvent;
    import flash.events.Event;
    import flash.events.EventDispatcher;
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.utils.Endian;
    import otlib.core.ClientFeatures;
    import otlib.core.Version;
    import otlib.core.VersionStorage;
    import otlib.sprites.SpriteReader;

    /** Reads small headers synchronously: no outstanding async streams or stale callbacks. */
    public class ClientInfoLoader extends EventDispatcher
    {
        private var m_clientInfo:ClientInfo;
        public function get clientInfo():ClientInfo { return m_clientInfo; }

        public function load(dat:File, spr:File, extended:Boolean):void
        {
            m_clientInfo = null;
            try
            {
                if (!dat || !spr || !dat.exists || !spr.exists)
                    throw new Error("DAT/SPR files not found.");
                var info:ClientInfo = new ClientInfo();
                info.features = new ClientFeatures(extended);
                // Prefer the matching descriptor, not an arbitrary OTFI in the folder.
                var dot:int = dat.name.lastIndexOf(".");
                var descriptor:File = dat.parent.resolvePath(dat.name.substring(0, dot) + ".otfi");
                var otfi:OTFI = new OTFI();
                if (descriptor.exists && otfi.load(descriptor))
                {
                    info.features.copyFrom(otfi.features);
                    info.spriteSize = otfi.spriteSize;
                    info.spriteDataSize = otfi.spriteDataSize;
                }
                var stream:FileStream = new FileStream();
                try
                {
                    stream.open(dat, FileMode.READ);
                    stream.endian = Endian.LITTLE_ENDIAN;
                    if (dat.size < (info.features.extendedItems ? 14 : 12)) throw new Error("Truncated DAT header.");
                    info.datSignature = stream.readUnsignedInt();
                    info.maxItemId = info.features.extendedItems ? stream.readUnsignedInt() : stream.readUnsignedShort();
                    info.maxOutfitId = stream.readUnsignedShort();
                    info.maxEffectId = stream.readUnsignedShort();
                    info.maxMissileId = stream.readUnsignedShort();
                }
                finally { try { stream.close(); } catch (closeError:Error) {} }
                var multi:Boolean = spr.name.toLowerCase() == "spr_index.dat";
                if (multi) info.features.extended = true;
                // Read the signature first to choose version-dependent header width.
                var signature:uint;
                if (!multi)
                {
                    try
                    {
                        stream.open(spr, FileMode.READ);
                        if (spr.size < 6) throw new Error("Truncated SPR header.");
                        signature = stream.readUnsignedInt();
                    }
                    finally { try { stream.close(); } catch (closeSprError:Error) {} }
                }
                else
                {
                    var manifest:File = spr.parent.resolvePath("spr_parts.dat");
                    try
                    {
                        stream.open(manifest, FileMode.READ);
                        if (manifest.size < 16 || stream.readUTFBytes(4) != "SPMT")
                            throw new Error("Invalid multi-SPR manifest.");
                        stream.readUnsignedInt();
                        signature = stream.readUnsignedInt();
                    }
                    finally { try { stream.close(); } catch (closeManifestError:Error) {} }
                }
                var version:Version = VersionStorage.getInstance().getBySignatures(info.datSignature, signature);
                if (!version) throw new Error("Unsupported DAT/SPR signatures.");
                info.features.applyVersionDefaults(version.value);
                var reader:SpriteReader = new SpriteReader(info.features);
                try
                {
                    reader.openSpriteFile(spr);
                    info.sprSignature = reader.readSignature();
                    info.maxSpriteId = reader.readSpriteCount();
                    // A folder may contain an older RGB archive and a newer RGBA export.
                    // Only override a stale descriptor when sampled records unambiguously
                    // validate in the opposite encoding; empty/ambiguous records do not vote.
                    info.features.transparency = reader.detectTransparency();
                }
                finally { reader.closeSpriteFile(); }
                info.clientVersion = version.value;
                info.clientVersionStr = version.valueStr;
                info.multiSpr = multi;
                m_clientInfo = info;
            }
            catch (error:Error)
            {
                dispatchEvent(new ErrorEvent(ErrorEvent.ERROR, false, false, error.message, error.errorID));
                return;
            }
            // Exactly one terminal event, after every stream has been closed.
            dispatchEvent(new Event(Event.COMPLETE));
        }
    }
}
