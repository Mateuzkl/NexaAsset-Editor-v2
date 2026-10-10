package otlib.sprites
{
    /** Test-only access to package-internal compressed records; no production API change. */
    public final class SpriteRecordComparison
    {
        public static function equal(a:Sprite, b:Sprite):Boolean
        {
            if (!a || !b) return !a && !b;
            if (a.compressedPixels.length != b.compressedPixels.length) return false;
            for (var i:uint = 0; i < a.compressedPixels.length; i++)
                if (a.compressedPixels[i] != b.compressedPixels[i]) return false;
            return true;
        }
    }
}
