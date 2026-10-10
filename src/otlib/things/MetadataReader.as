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

package otlib.things
{
    import com.mignari.errors.NotImplementedMethodError;

    import flash.filesystem.FileStream;
    import flash.utils.Endian;

    import otlib.animation.FrameDuration;
    import otlib.animation.FrameGroup;
    import com.mignari.utils.StringUtil;
    import otlib.utils.SpriteExtent;
    import ob.settings.ObjectBuilderSettings;
    import otlib.core.ClientFeatures;

    public class MetadataReader extends FileStream implements IMetadataReader
    {
        private var _settings:ObjectBuilderSettings;
        private var _features:ClientFeatures;

        // --------------------------------------------------------------------------
        // CONSTRUCTOR
        // --------------------------------------------------------------------------

        public function MetadataReader()
        {
            endian = Endian.LITTLE_ENDIAN;
        }

        // --------------------------------------------------------------------------
        // METHODS
        // --------------------------------------------------------------------------

        // --------------------------------------
        // Public
        // --------------------------------------

        public function get settings():ObjectBuilderSettings
        {
            return _settings;
        }
        public function set settings(value:ObjectBuilderSettings):void
        {
            if (_settings != value)
                _settings = value;
        }

        public function get features():ClientFeatures
        {
            return _features;
        }
        public function set features(value:ClientFeatures):void
        {
            _features = value;
        }

        public function readSignature():uint
        {
            position = MetadataFilePosition.SIGNATURE;
            return readUnsignedInt();
        }

        public function readItemsCount():uint
        {
            position = MetadataFilePosition.ITEMS_COUNT;
            return _features && _features.extendedItems ? readUnsignedInt() : readUnsignedShort();
        }

        public function readOutfitsCount():uint
        {
            position = MetadataFilePosition.OUTFITS_COUNT + (_features && _features.extendedItems ? 2 : 0);
            return readUnsignedShort();
        }

        public function readEffectsCount():uint
        {
            position = MetadataFilePosition.EFFECTS_COUNT + (_features && _features.extendedItems ? 2 : 0);
            return readUnsignedShort();
        }

        public function readMissilesCount():uint
        {
            position = MetadataFilePosition.MISSILES_COUNT + (_features && _features.extendedItems ? 2 : 0);
            return readUnsignedShort();
        }

        public function readProperties(type:ThingType):Boolean
        {
            throw new NotImplementedMethodError();
        }

        public function readTexturePatterns(type:ThingType):Boolean
        {
            var extended:Boolean = _features ? _features.extended : false;
            var frameDurations:Boolean = _features ? _features.improvedAnimations : false;
            var frameGroups:Boolean = _features ? _features.frameGroups : false;

            var groupCount:uint = 1;
            if (frameGroups && type.category == ThingCategory.OUTFIT)
            {
                groupCount = readUnsignedByte();
            }

            var i:uint;
            var groupType:uint;
            var frameGroup:FrameGroup;
            for (groupType = 0; groupType < groupCount; groupType++)
            {
                if (frameGroups && type.category == ThingCategory.OUTFIT)
                    readUnsignedByte();

                frameGroup = new FrameGroup();
                frameGroup.width = readUnsignedByte();
                frameGroup.height = readUnsignedByte();

                if (frameGroup.width > 1 || frameGroup.height > 1)
                    frameGroup.exactSize = readUnsignedByte();
                else
                    frameGroup.exactSize = SpriteExtent.DEFAULT_SIZE;

                frameGroup.layers = readUnsignedByte();
                frameGroup.patternX = readUnsignedByte();
                frameGroup.patternY = readUnsignedByte();
                frameGroup.patternZ = readUnsignedByte();
                frameGroup.frames = readUnsignedByte();
                if (frameGroup.frames > 1)
                {
                    frameGroup.isAnimation = true;
                    frameGroup.frameDurations = new Vector.<FrameDuration>(frameGroup.frames, true);

                    if (frameDurations)
                    {
                        frameGroup.animationMode = readUnsignedByte();
                        frameGroup.loopCount = readInt();
                        frameGroup.startFrame = readByte();

                        for (i = 0; i < frameGroup.frames; i++)
                        {
                            var minimum:uint = readUnsignedInt();
                            var maximum:uint = readUnsignedInt();
                            frameGroup.frameDurations[i] = new FrameDuration(minimum, maximum);
                        }
                    }
                    else
                    {
                        var duration:uint = settings.getDefaultDuration(type.category);
                        for (i = 0; i < frameGroup.frames; i++)
                            frameGroup.frameDurations[i] = new FrameDuration(duration, duration);
                    }
                }

                var totalSprites:uint = frameGroup.getTotalSprites();
                // Pixel bytes per sprite are unrelated to the number of DAT references.
                // Large 10.98 outfits legitimately reference more than 4,096 sprites.
                var refs:Number = Number(frameGroup.width) * frameGroup.height * frameGroup.layers *
                    frameGroup.patternX * frameGroup.patternY * frameGroup.patternZ * frameGroup.frames;
                if (!refs || refs > 65536 || refs * (extended ? 4 : 2) > bytesAvailable)
                    throw new Error("Too many sprite references in " + type.category + " " + type.id +
                        " at byte " + position + ": " + frameGroup.width + "x" + frameGroup.height +
                        ", layers=" + frameGroup.layers + ", patterns=" + frameGroup.patternX + "/" +
                        frameGroup.patternY + "/" + frameGroup.patternZ + ", frames=" + frameGroup.frames);

                frameGroup.spriteIndex = new Vector.<uint>(totalSprites);
                for (i = 0; i < totalSprites; i++)
                {
                    if (extended)
                        frameGroup.spriteIndex[i] = readUnsignedInt();
                    else
                        frameGroup.spriteIndex[i] = readUnsignedShort();
                }

                type.setFrameGroup(groupType, frameGroup);
            }

            return true;
        }
    }
}
