package ob.modern
{
    import flash.desktop.NativeProcess;
    import flash.desktop.NativeProcessStartupInfo;
    import flash.events.EventDispatcher;
    import flash.events.NativeProcessExitEvent;
    import flash.events.ProgressEvent;
    import flash.events.IOErrorEvent;
    import flash.events.TimerEvent;
    import flash.filesystem.File;
    import flash.utils.Timer;

    /** One bounded, serial JSON-lines connection. It never sends worker mutations. */
    public class ModernAssetSession extends EventDispatcher
    {
        public static const SCHEMA_VERSION:int = 1;
        private var process:NativeProcess;
        private var timer:Timer = new Timer(30000, 1);
        private var output:String = "";
        private var diagnostics:String = "";
        private var sequence:uint = 0;
        private var pending:String;
        private var sessionDirectory:File;
        private var sessionParent:File;
        public var generation:uint = 0;
        [Bindable] public var ready:Boolean = false;
        public function get busy():Boolean { return process != null && (!ready || pending != null); }
        public function get running():Boolean { return process != null && process.running; }
        public function get directory():File { return sessionDirectory; }

        public function ModernAssetSession() { timer.addEventListener(TimerEvent.TIMER_COMPLETE, timeoutHandler); }
        public function open(source:File, executable:File, temporaryParent:File):void
        {
            close(); generation++;
            if (!NativeProcess.isSupported) throw new Error("NativeProcess requires an extendedDesktop AIR bundle.");
            if (!source || !source.isDirectory || !executable || !executable.exists) throw new Error("Modern helper or assets folder is missing. Rebuild the portable bundle.");
            if (!temporaryParent) throw new Error("A writable temporary previews folder is required.");
            sessionParent = temporaryParent;
            try
            {
                if (sessionParent.url.indexOf("app:") == 0) throw new Error("The app:/ resource scheme is read-only.");
                sessionParent.createDirectory();
                var candidate:File = sessionParent.resolvePath("session-" + new Date().time + "-" + uint(Math.random() * uint.MAX_VALUE));
                if (candidate.exists) throw new Error("Temporary session collision.");
                candidate.createDirectory();
                sessionDirectory = candidate; // Own it only after successfully creating it.
            }
            catch (directoryError:Error)
            {
                var failedPath:String = sessionParent.nativePath;
                close();
                throw new Error("Cannot create temporary previews in '" + failedPath + "'. Move the portable application to a writable folder. " + directoryError.message);
            }
            var info:NativeProcessStartupInfo = new NativeProcessStartupInfo();
            info.executable = executable;
            info.arguments = Vector.<String>(["--input", source.nativePath, "--output", sessionDirectory.nativePath]);
            info.workingDirectory = executable.parent;
            process = new NativeProcess();
            process.addEventListener(ProgressEvent.STANDARD_OUTPUT_DATA, stdoutHandler);
            process.addEventListener(ProgressEvent.STANDARD_ERROR_DATA, stderrHandler);
            process.addEventListener(NativeProcessExitEvent.EXIT, exitHandler);
            process.addEventListener(IOErrorEvent.STANDARD_INPUT_IO_ERROR, ioErrorHandler);
            process.addEventListener(IOErrorEvent.STANDARD_OUTPUT_IO_ERROR, ioErrorHandler);
            process.addEventListener(IOErrorEvent.STANDARD_ERROR_IO_ERROR, ioErrorHandler);
            try { process.start(info); armTimeout(); }
            catch (error:Error) { close(); throw error; }
        }
        public function request(command:String, fields:Object = null):String
        {
            if (!ready || !running || pending != null) throw new Error("Modern session is not ready or is busy.");
            var request:Object = fields || {};
            pending = String(++sequence); request.requestId = pending;
            request.generation = generation; request.command = command;
            try { process.standardInput.writeUTFBytes(JSON.stringify(request) + "\n"); armTimeout(); }
            catch (error:Error) { fail(error.message); throw error; }
            return pending;
        }
        private function stdoutHandler(event:ProgressEvent):void
        {
            if (event.currentTarget != process) return;
            try
            {
                while (process.standardOutput.bytesAvailable > 0)
                {
                    // Decode UTF-8 only after a complete line, not across byte chunk boundaries.
                    // Helper JSON escapes non-ASCII by default, so every emitted line is ASCII.
                    output += process.standardOutput.readUTFBytes(Math.min(32768, process.standardOutput.bytesAvailable));
                    if (output.length > 1048576) throw new Error("Modern helper response exceeds 1 MiB.");
                    var newline:int;
                    while ((newline = output.indexOf("\n")) >= 0)
                    {
                        var line:String = output.substr(0, newline); output = output.substr(newline + 1);
                        if (line.length > 0) accept(JSON.parse(line));
                        if (!process) return;
                    }
                }
            }
            catch (error:Error) { fail("Invalid modern helper response: " + error.message); }
        }
        private function accept(message:Object):void
        {
            if (!message || message.schemaVersion != SCHEMA_VERSION) throw new Error("Incompatible modern protocol version.");
            if (message.type == "progress" && (!ready || (pending != null && message.generation == generation && message.requestId == pending))) { armTimeout(); }
            else if (message.type == "ready" && !ready) { ready = true; timer.reset(); }
            else if (message.type == "error" && !ready) { fail(String(message.message)); return; }
            else if ((message.type == "result" || message.type == "error") &&
                message.generation == generation && message.requestId == pending && pending != null)
            { pending = null; timer.reset(); }
            else return; // stale generations or unsolicited completions cannot update the UI
            dispatchEvent(new ModernAssetEvent(message));
        }
        private function stderrHandler(event:ProgressEvent):void
        {
            if (event.currentTarget != process) return;
            while (process.standardError.bytesAvailable > 0)
            {
                diagnostics += process.standardError.readUTFBytes(Math.min(8192, process.standardError.bytesAvailable));
                if (diagnostics.length > 65536) diagnostics = diagnostics.substr(diagnostics.length - 65536);
            }
        }
        private function exitHandler(event:NativeProcessExitEvent):void
        { if (event.currentTarget == process) fail("Modern helper exited (" + event.exitCode + "). " + diagnostics.substr(0, 512)); }
        private function ioErrorHandler(event:IOErrorEvent):void { if (event.currentTarget == process) fail(event.text); }
        private function timeoutHandler(event:TimerEvent):void { fail("Modern helper timed out. The session was stopped; source assets were not modified."); }
        private function armTimeout():void { timer.reset(); timer.start(); }
        private function fail(message:String):void { close(); dispatchEvent(new ModernAssetEvent({type:"error", message:message})); }
        public function close():void
        {
            generation++; ready = false; pending = null; timer.reset();
            var old:NativeProcess = process; process = null;
            if (old)
            {
                old.removeEventListener(ProgressEvent.STANDARD_OUTPUT_DATA, stdoutHandler);
                old.removeEventListener(ProgressEvent.STANDARD_ERROR_DATA, stderrHandler);
                old.removeEventListener(NativeProcessExitEvent.EXIT, exitHandler);
                old.removeEventListener(IOErrorEvent.STANDARD_INPUT_IO_ERROR, ioErrorHandler);
                old.removeEventListener(IOErrorEvent.STANDARD_OUTPUT_IO_ERROR, ioErrorHandler);
                old.removeEventListener(IOErrorEvent.STANDARD_ERROR_IO_ERROR, ioErrorHandler);
                try { if (old.running) old.exit(true); } catch (ignored:Error) { }
            }
            output = diagnostics = "";
            if (sessionDirectory && sessionParent && sessionDirectory.parent.nativePath == sessionParent.nativePath && sessionDirectory.name.indexOf("session-") == 0)
            {
                // Delete only our fixed preview files, never recurse into source/user folders.
                for each (var name:String in ["preview-0.png", "preview-1.png", "native-things.bin", "native-sprites.bin", "native-frame.bin", "native-obd.bin"])
                {
                    var preview:File = sessionDirectory.resolvePath(name);
                    try { if (preview.exists && !preview.isDirectory) preview.deleteFile(); } catch (fileError:Error) { }
                }
                try { if (sessionDirectory.exists) sessionDirectory.deleteDirectory(false); } catch (directoryError:Error) { }
            }
            sessionDirectory = sessionParent = null;
        }
    }
}
