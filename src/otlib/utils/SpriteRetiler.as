package otlib.utils
{
    import flash.utils.ByteArray;
    import flash.utils.Dictionary;
    import otlib.animation.FrameGroup;
    import otlib.sprites.SpriteData;
    import otlib.things.ThingData;
    import otlib.things.ThingType;

    /** Lossless, bottom-right anchored retiling. Never rescales artwork. */
    public final class SpriteRetiler
    {
        private static function dimensions(group:FrameGroup, from:uint, to:uint):void
        {
            if ((from != 32 && from != 64) || (to != 32 && to != 64))
                throw new Error("Lossless retiling supports 32 and 64 pixel tiles only.");
            var width:uint = Math.ceil(group.width * from / to);
            var height:uint = Math.ceil(group.height * from / to);
            if (!width || !height || width > 255 || height > 255 || Number(width) * height * group.getTotalTextures() > 65536)
                throw new Error("Retiled frame group exceeds the safe DAT/OBD layout limit.");
            group.width = width; group.height = height;
            // exactSize, offsets, phases, layers, patterns and group types stay in physical pixels.
        }

        public static function archiveThing(source:ThingType):ThingType
        {
            var result:ThingType = source.clone();
            for each (var group:FrameGroup in result.frameGroups)
            {
                if (!group) continue;
                var oldWidth:uint = group.width, oldHeight:uint = group.height;
                var ids:Vector.<uint> = group.spriteIndex;
                dimensions(group, 64, 32);
                group.spriteIndex = new Vector.<uint>(group.getTotalSprites(), true);
                for (var t:uint = 0; t < group.getTotalTextures(); t++)
                    for (var y:uint = 0; y < group.height; y++)
                        for (var x:uint = 0; x < group.width; x++)
                        {
                            var id:uint = ids[(t * oldHeight + (y >> 1)) * oldWidth + (x >> 1)];
                            if (id > 0x3fffffff) throw new Error("Retiled sprite ID exceeds 32 bits.");
                            group.spriteIndex[(t * group.height + y) * group.width + x] = id ? (id - 1) * 4 + (y % 2) * 2 + x % 2 + 1 : 0;
                        }
            }
            return result;
        }

        /** part 0 is bottom-right, matching legacy DAT tile ordering. */
        public static function split64(pixels:ByteArray, part:uint):ByteArray
        {
            if (!pixels || pixels.length != 16384 || part > 3) throw new Error("Invalid 64x64 tile.");
            var result:ByteArray = new ByteArray();
            var left:uint = (1 - part % 2) * 32, top:uint = (1 - (part >> 1)) * 32;
            for (var y:uint = 0; y < 32; y++) result.writeBytes(pixels, ((top + y) * 64 + left) * 4, 128);
            result.position = 0;
            return result;
        }

        public static function data(source:ThingData, from:uint, to:uint):ThingData
        {
            if (!source || !source.thing || (from != 32 && from != 64) || (to != 32 && to != 64)) throw new Error("Invalid lossless sprite conversion.");
            var previousSize:uint = SpriteExtent.DEFAULT_SIZE, previousData:uint = SpriteExtent.DEFAULT_DATA_SIZE;
            SpriteExtent.DEFAULT_SIZE = to; SpriteExtent.DEFAULT_DATA_SIZE = to * to * 4;
            var result:ThingData;
            try
            {
                result = new ThingData();
                result.obdVersion = source.obdVersion; result.clientVersion = source.clientVersion;
                result.thing = source.thing.clone(); result.xmlAttributes = source.xmlAttributes;
                var groups:Dictionary = result.sprites;
                for each (var group:FrameGroup in result.thing.frameGroups)
                {
                    if (!group) continue;
                    var old:FrameGroup = source.thing.getFrameGroup(group.type);
                    var tiles:Vector.<SpriteData> = source.sprites[group.type];
                    if (!tiles || tiles.length != old.getTotalSprites()) throw new Error("Incomplete sprite group.");
                    dimensions(group, from, to);
                    group.spriteIndex = new Vector.<uint>(group.getTotalSprites(), true);
                    var output:Vector.<SpriteData> = new Vector.<SpriteData>(group.getTotalSprites(), true);
                    groups[group.type] = output;
                    for (var t:uint = 0; t < group.getTotalTextures(); t++)
                        for (var ny:uint = 0; ny < group.height; ny++)
                            for (var nx:uint = 0; nx < group.width; nx++)
                            {
                                var bytes:ByteArray = new ByteArray(); bytes.length = to * to * 4;
                                // Coordinates measure distance from the bottom-right anchor.
                                for (var oy:uint = uint(ny * to / from); oy < Math.min(old.height, Math.ceil((ny + 1) * to / from)); oy++)
                                    for (var ox:uint = uint(nx * to / from); ox < Math.min(old.width, Math.ceil((nx + 1) * to / from)); ox++)
                                    {
                                        var tile:SpriteData = tiles[(t * old.height + oy) * old.width + ox];
                                        if (!tile || !tile.pixels || tile.pixels.length != from * from * 4) throw new Error("Invalid source tile size.");
                                        var left:int = Math.max(-(nx + 1) * to, -(ox + 1) * from);
                                        var right:int = Math.min(-nx * to, -ox * from);
                                        var top:int = Math.max(-(ny + 1) * to, -(oy + 1) * from);
                                        var bottom:int = Math.min(-ny * to, -oy * from);
                                        for (var row:int = top; row < bottom; row++)
                                        {
                                            bytes.position = ((row + (ny + 1) * to) * to + left + (nx + 1) * to) * 4;
                                            bytes.writeBytes(tile.pixels, ((row + (oy + 1) * from) * from + left + (ox + 1) * from) * 4, (right - left) * 4);
                                        }
                                    }
                                var index:uint = (t * group.height + ny) * group.width + nx;
                                var sprite:SpriteData = new SpriteData(); sprite.id = index + 1;
                                bytes.position = 0; sprite.pixels = bytes;
                                output[index] = sprite; group.spriteIndex[index] = sprite.id;
                            }
                }
            }
            catch (error:Error) { if (result) dispose(result); throw error; }
            finally { SpriteExtent.DEFAULT_SIZE = previousSize; SpriteExtent.DEFAULT_DATA_SIZE = previousData; }
            return result;
        }

        public static function dispose(value:ThingData):void
        {
            for each (var group:Vector.<SpriteData> in value.sprites)
                for each (var sprite:SpriteData in group) if (sprite) sprite.dispose();
        }
    }
}
