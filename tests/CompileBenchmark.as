package
{
    import flash.desktop.NativeApplication;
    import flash.display.Sprite;
    import flash.events.InvokeEvent;
    import flash.events.ErrorEvent;
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.utils.getTimer;
    import mx.resources.ResourceManager;
    import otlib.core.ClientFeatures;
    import otlib.core.Version;
    import otlib.sprites.SpriteStorage;
    import otlib.resources.Resources;
    [ResourceBundle("strings")]
    public class CompileBenchmark extends Sprite
    {
        private var errorText:String;
        public function CompileBenchmark()
        {
            Resources.manager = ResourceManager.getInstance();
            NativeApplication.nativeApplication.addEventListener(InvokeEvent.INVOKE, run);
        }
        private function capture(event:ErrorEvent):void { errorText = event.text; }
        private function run(event:InvokeEvent):void
        {
            NativeApplication.nativeApplication.removeEventListener(InvokeEvent.INVOKE, run);
            var exitCode:int = 0;
            var storage:SpriteStorage = new SpriteStorage();
            var output:File = new File(event.arguments[1]); output.createDirectory();
            var result:Object;
            try
            {
                var version:Version = new Version(); version.value = 860;
                version.datSignature = 0x4C2C7993; version.sprSignature = 0x4C220594;
                var features:ClientFeatures = new ClientFeatures(true, false, true, true);
                storage.addEventListener(ErrorEvent.ERROR, capture);
                storage.load(new File(event.arguments[0]), version, features);
                var started:int = getTimer();
                if (!storage.compile(output.resolvePath("Tibia.spr"), version, features)) throw new Error(errorText);
                result = {sprites:storage.spritesCount, milliseconds:getTimer() - started};
                trace(JSON.stringify(result));
            }
            catch (error:Error) { exitCode = 1; result = {error:error.getStackTrace()}; trace(JSON.stringify(result)); }
            finally
            {
                storage.unload();
                var stream:FileStream = new FileStream();
                try { stream.open(output.resolvePath("benchmark.json"), FileMode.WRITE); stream.writeUTFBytes(JSON.stringify(result)); }
                finally { stream.close(); }
                NativeApplication.nativeApplication.exit(exitCode);
            }
        }
    }
}
