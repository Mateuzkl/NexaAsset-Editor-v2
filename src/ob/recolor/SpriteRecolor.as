package ob.recolor
{
    import flash.utils.ByteArray;
    import otlib.animation.FrameGroup;
    import otlib.utils.SpriteExtent;

    /** Shared by the preview and the worker. Operates on straight ARGB bytes. */
    public final class SpriteRecolor
    {
        public static function distance(a:uint, b:uint):int
        {
            return Math.max(Math.abs((a >> 16 & 255) - (b >> 16 & 255)),
                Math.abs((a >> 8 & 255) - (b >> 8 & 255)), Math.abs((a & 255) - (b & 255)));
        }

        public static function maskKey(group:uint, texture:uint):String
        {
            return group + ":" + texture;
        }

        public static function applyTile(pixels:ByteArray, options:Object, mask:ByteArray,
                                         group:FrameGroup, tile:uint):ByteArray
        {
            var size:int = SpriteExtent.DEFAULT_SIZE;
            var width:int = group.width * size;
            var left:int = (group.width - 1 - tile % group.width) * size;
            var top:int = (group.height - 1 - int(tile / group.width)) * size;
            var result:ByteArray;
            var source:uint = options.source;
            var target:uint = options.target;
            var targetHsl:Vector.<Number>;
            var sourceLight:Number;
            if (options.replace && options.shading)
            {
                targetHsl = toHsl(target);
                sourceLight = lightness(source);
            }

            var adjust:Boolean = options.hsl && (options.colorize || options.hue != 0 ||
                options.saturation != 0 || options.lightness != 0);
            for (var y:int = 0; y < size; y++)
            {
                var maskStart:int = (top + y) * width + left;
                for (var x:int = 0; x < size; x++)
                {
                    var at:int = (y * size + x) * 4;
                    if (!pixels[at] || (mask && !mask[maskStart + x]))
                        continue;
                    var original:uint = pixels[at + 1] << 16 | pixels[at + 2] << 8 | pixels[at + 3];
                    var rgb:uint = original;
                    if (options.replace && distance(original, source) <= int(options.tolerance))
                    {
                        rgb = targetHsl && source != target ? fromHsl(targetHsl[0], targetHsl[1],
                            clamp(targetHsl[2] + lightness(original) - sourceLight)) : target;
                        if (targetHsl && source == target)
                            rgb = original;
                    }
                    if (adjust)
                    {
                        var hsl:Vector.<Number> = toHsl(rgb);
                        var hue:Number = (options.colorize ? 0 : hsl[0]) + Number(options.hue) / 360;
                        var saturation:Number = (options.colorize ? 0.5 : hsl[1]);
                        rgb = fromHsl(hue, offset(saturation, Number(options.saturation) / 100),
                            offset(hsl[2], Number(options.lightness) / 100));
                    }
                    if (rgb == original)
                        continue;
                    if (!result)
                    {
                        result = new ByteArray();
                        result.writeBytes(pixels);
                        result.position = 0;
                    }
                    result[at + 1] = rgb >> 16 & 255;
                    result[at + 2] = rgb >> 8 & 255;
                    result[at + 3] = rgb & 255;
                }
            }
            return result;
        }

        private static function offset(value:Number, change:Number):Number
        {
            return clamp(value + change * (change < 0 ? value : 1 - value));
        }

        private static function clamp(value:Number):Number
        {
            return Math.max(0, Math.min(1, value));
        }

        private static function lightness(color:uint):Number
        {
            var r:uint = color >> 16 & 255;
            var g:uint = color >> 8 & 255;
            var b:uint = color & 255;
            return (Math.max(r, g, b) + Math.min(r, g, b)) / 510;
        }

        private static function toHsl(color:uint):Vector.<Number>
        {
            var r:Number = (color >> 16 & 255) / 255;
            var g:Number = (color >> 8 & 255) / 255;
            var b:Number = (color & 255) / 255;
            var high:Number = Math.max(r, g, b);
            var low:Number = Math.min(r, g, b);
            var delta:Number = high - low;
            var light:Number = (high + low) / 2;
            if (delta == 0) return new <Number>[0, 0, light];
            var hue:Number = high == r ? (g - b) / delta :
                high == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            return new <Number>[hue / 6, delta / (1 - Math.abs(2 * light - 1)), light];
        }

        private static function fromHsl(hue:Number, saturation:Number, light:Number):uint
        {
            var chroma:Number = (1 - Math.abs(2 * light - 1)) * saturation;
            var base:Number = light - chroma / 2;
            return channel(hue, 2, base, chroma) << 16 |
                channel(hue, 0, base, chroma) << 8 | channel(hue, 4, base, chroma);
        }

        private static function channel(hue:Number, phase:int, base:Number, chroma:Number):uint
        {
            var k:Number = ((hue - Math.floor(hue)) * 6 + phase) % 6;
            return Math.round(255 * (base + chroma * Math.max(0, Math.min(k, 4 - k, 1))));
        }
    }
}
