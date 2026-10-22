package
{
    import flash.desktop.NativeApplication;
    import flash.display.Sprite;
    import flash.events.InvokeEvent;
    import flash.events.TimerEvent;
    import flash.filesystem.File;
    import flash.filesystem.FileMode;
    import flash.filesystem.FileStream;
    import flash.utils.Timer;
    import otlib.utils.AssetOutputTransaction;
    import otlib.utils.AssetPublication;

    public class AssetPublicationTests extends Sprite
    {
        private var output:File, assetRoot:File, staging:File, lock:FileStream;
        private var names:Array = ["Tibia.dat", "Tibia.spr", "Tibia.otfi"];
        private var lines:Array = [], completed:uint, failed:uint, reports:uint;
        private var watchdog:Timer = new Timer(25000, 1);
        public function AssetPublicationTests()
        {
            NativeApplication.nativeApplication.addEventListener(InvokeEvent.INVOKE, run);
            watchdog.addEventListener(TimerEvent.TIMER_COMPLETE, function(e:TimerEvent):void { finish(new Error("Publication test hung.")); });
        }
        private function check(value:Boolean, message:String):void { if (!value) throw new Error(message); }
        private function guard(callback:Function):void { try { callback(); } catch(error:Error) { finish(error); } }
        private function write(file:File, text:String):void
        {
            var stream:FileStream = new FileStream();
            try { stream.open(file, FileMode.WRITE); stream.writeUTFBytes(text); } finally { stream.close(); }
        }
        private function read(file:File):String
        {
            var stream:FileStream = new FileStream(); var result:String;
            try { stream.open(file, FileMode.READ); result = stream.readUTFBytes(stream.bytesAvailable); } finally { stream.close(); }
            return result;
        }
        private function prepare(label:String):void
        {
            assetRoot = output.resolvePath(label); assetRoot.createDirectory();
            staging = AssetOutputTransaction.createStage(assetRoot);
            for each(var name:String in names) { write(assetRoot.resolvePath(name), "old-" + name); write(staging.resolvePath(name), "new-" + name); }
            completed = failed = reports = 0;
        }
        private function run(event:InvokeEvent):void
        {
            NativeApplication.nativeApplication.removeEventListener(InvokeEvent.INVOKE, run);
            output = new File(event.arguments[0]); output.createDirectory(); watchdog.start();
            guard(function():void {
                prepare("success");
                var publisher:AssetPublication = new AssetPublication(staging, assetRoot, names,
                    function(backup:File):void { guard(function():void {
                        completed++; check(completed == 1 && failed == 0, "Success callback count.");
                        for each(var name:String in names) {
                            check(read(assetRoot.resolvePath(name)) == "new-" + name, "New generation missing.");
                            check(read(backup.resolvePath(name)) == "old-" + name, "Backup missing.");
                        }
                        check(reports >= 6, "Per-file progress missing."); lines.push("PASS: asynchronous publication, backup and progress"); testLock();
                    }); }, function(error:Error):void { finish(error); },
                    function(done:uint, total:uint, label:String):void { reports++; });
                publisher.start(); publisher.start(); // Reentrance must not start another publication.
            });
        }
        private function testLock():void
        {
            prepare("locked"); lock = new FileStream(); lock.open(assetRoot.resolvePath("Tibia.spr"), FileMode.READ);
            new AssetPublication(staging, assetRoot, names, function(backup:File):void { finish(new Error("Locked target reported success.")); },
                function(error:Error):void { guard(function():void {
                    failed++; check(failed == 1, "Duplicate failure.");
                    for each(var name:String in names) {
                        check(read(assetRoot.resolvePath(name)) == "old-" + name, "Locked publication lost old data.");
                        check(read(staging.resolvePath(name)) == "new-" + name, "Locked publication lost staged data.");
                    }
                    lock.close(); lock = null;
                    lines.push("PASS: locked target terminates with error and rolls back");
                    new AssetPublication(staging, assetRoot, names, function(backup:File):void { guard(function():void {
                        check(read(assetRoot.resolvePath("Tibia.spr")) == "new-Tibia.spr", "Retry failed.");
                        lines.push("PASS: retry after releasing file lock"); testTimeout();
                    }); }, function(retryError:Error):void { finish(retryError); }).start();
                }); }, null, 3000).start();
        }
        private function testTimeout():void
        {
            prepare("timeout"); var stalled:Boolean;
            new AssetPublication(staging, assetRoot, names, function(backup:File):void { finish(new Error("Stalled publication reported success.")); },
                function(error:Error):void { guard(function():void {
                    check(error.message.indexOf("timed out") >= 0, "Timeout was not reported.");
                    for each(var name:String in names) check(read(assetRoot.resolvePath(name)) == "old-" + name, "Timeout rollback lost original.");
                    lines.push("PASS: stalled move deadline, cancellation and reverse rollback"); testInvalid();
                }); }, null, 1000, function(from:File, to:File):void {
                    if (!stalled && from.name == "Tibia.spr") { stalled = true; return; }
                    from.moveToAsync(to, false);
                }).start();
        }
        private function testInvalid():void
        {
            prepare("invalid");
            new AssetPublication(staging, assetRoot, ["../outside"], function(backup:File):void { finish(new Error("Unsafe name accepted.")); },
                function(error:Error):void { guard(function():void {
                    check(read(assetRoot.resolvePath("Tibia.dat")) == "old-Tibia.dat", "Invalid input mutated destination.");
                    lines.push("PASS: unsafe paths rejected without mutation"); finish();
                }); }).start();
        }
        private function finish(error:Error = null):void
        {
            watchdog.stop(); if (lock) { lock.close(); lock = null; }
            if (error) lines.push("FAIL: " + error.message + "\n" + error.getStackTrace());
            write(output.resolvePath("publication-result.txt"), lines.join("\n"));
            NativeApplication.nativeApplication.exit(error ? 1 : 0);
        }
    }
}
