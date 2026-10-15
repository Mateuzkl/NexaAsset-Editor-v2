package ob.modern
{
    import flash.display.BitmapData;
    import otlib.sprites.SpriteData;
    /** A real 32/64-pixel modern sprite, not a truncated legacy tile. */
    public class ModernSpriteData extends SpriteData
    {
        public var imageWidth:int;
        public var imageHeight:int;
        public override function getBitmap(backgroundColor:uint=0):BitmapData
        {
            if (!pixels) return null;
            var bitmap:BitmapData=new BitmapData(imageWidth,imageHeight,true,backgroundColor);
            pixels.position=0;bitmap.setPixels(bitmap.rect,pixels);return bitmap;
        }
    }
}
