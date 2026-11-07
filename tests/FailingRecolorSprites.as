package
{
    import flash.utils.ByteArray;
    import otlib.core.otlib_internal;
    import otlib.sprites.SpriteStorage;
    import otlib.utils.ChangeResult;

    use namespace otlib_internal;

    public class FailingRecolorSprites extends SpriteStorage
    {
        public var successfulAdds:int = int.MAX_VALUE;

        override otlib_internal function internalAddSprite(pixels:ByteArray, result:ChangeResult = null):ChangeResult
        {
            if (successfulAdds-- <= 0)
                return new ChangeResult(null, false, "Injected sprite allocation failure");
            return super.internalAddSprite(pixels, result);
        }
    }
}
