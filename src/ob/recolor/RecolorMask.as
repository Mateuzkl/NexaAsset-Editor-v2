package ob.recolor
{
    import flash.display.BitmapData;
    import flash.geom.Rectangle;
    import flash.utils.ByteArray;

    /** One binary selection in the rendered texture's pixel coordinates. */
    public final class RecolorMask
    {
        public const pixels:ByteArray = new ByteArray();
        private var width:int;
        private var height:int;

        public function RecolorMask(width:int, height:int)
        {
            this.width = width;
            this.height = height;
            pixels.length = width * height;
        }

        public function fill(value:Boolean):void
        {
            for (var i:int = 0; i < pixels.length; i++)
                pixels[i] = value ? 1 : 0;
        }

        public function invert():void
        {
            for (var i:int = 0; i < pixels.length; i++)
                pixels[i] = 1 - pixels[i];
        }

        public function rectangle(x:int, y:int, endX:int, endY:int, mark:Boolean = true):void
        {
            var left:int = Math.max(0, Math.min(x, endX));
            var top:int = Math.max(0, Math.min(y, endY));
            var right:int = Math.min(width - 1, Math.max(x, endX));
            var bottom:int = Math.min(height - 1, Math.max(y, endY));
            for (var row:int = top; row <= bottom; row++)
                for (var col:int = left; col <= right; col++)
                    pixels[row * width + col] = mark ? 1 : 0;
        }

        public function stroke(x:int, y:int, endX:int, endY:int, size:int, mark:Boolean):void
        {
            var dx:int = endX - x;
            var dy:int = endY - y;
            var steps:int = Math.max(1, Math.abs(dx), Math.abs(dy));
            var offset:int = (size - 1) / 2;
            for (var i:int = 0; i <= steps; i++)
            {
                var left:int = x + Math.round(dx * i / steps) - offset;
                var top:int = y + Math.round(dy * i / steps) - offset;
                rectangle(left, top, left + size - 1, top + size - 1, mark);
            }
        }

        public function wand(source:BitmapData, x:int, y:int, tolerance:int, add:Boolean):void
        {
            if (!add) fill(false);
            if (x < 0 || x >= width || y < 0 || y >= height || source.width != width || source.height != height)
                throw new ArgumentError("Wand coordinates or bitmap dimensions do not match the mask.");
            var seed:uint = source.getPixel32(x, y);
            if (!(seed >>> 24))
                return;

            var visited:ByteArray = new ByteArray();
            visited.length = pixels.length;
            var queue:Vector.<int> = new <int>[y * width + x];
            visited[queue[0]] = 1;
            for (var cursor:int = 0; cursor < queue.length; cursor++)
            {
                var index:int = queue[cursor];
                var col:int = index % width;
                var row:int = index / width;
                var color:uint = source.getPixel32(col, row);
                if (!(color >>> 24) || SpriteRecolor.distance(seed, color) > tolerance)
                    continue;
                pixels[index] = 1;
                if (col > 0) enqueue(index - 1);
                if (col + 1 < width) enqueue(index + 1);
                if (row > 0) enqueue(index - width);
                if (row + 1 < height) enqueue(index + width);
            }

            function enqueue(index:int):void
            {
                if (visited[index]) return;
                visited[index] = 1;
                queue.push(index);
            }
        }

        public function drawOverlay(bitmap:BitmapData, area:Rectangle):void
        {
            bitmap.lock();
            bitmap.fillRect(bitmap.rect, 0);
            for (var i:int = 0; i < pixels.length; i++)
                if (pixels[i])
                    bitmap.setPixel32(i % width, int(i / width), 0x9400DCFF);
            if (area)
                bitmap.fillRect(area, 0x9400DCFF);
            bitmap.unlock();
        }
    }
}
