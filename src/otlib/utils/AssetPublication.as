package otlib.utils
{
    import flash.events.Event;
    import flash.events.IOErrorEvent;
    import flash.events.SecurityErrorEvent;
    import flash.events.TimerEvent;
    import flash.filesystem.File;
    import flash.utils.Timer;

    /** Non-blocking publication with per-file deadlines and reverse-order rollback.
     * Retains backup/staging files on recovery failure. Never copies a large SPR.
     */
    public final class AssetPublication
    {
        private var stage:File, root:File, backup:File;
        private var names:Array, saved:Array = [], installed:Array = [], recovery:Array;
        private var at:int, moves:int, total:int;
        private var active:File, original:File, destination:File;
        private var kind:String, name:String, failure:String;
        private var finished:Boolean, rollingBack:Boolean, started:Boolean;
        private var timer:Timer;
        private var onComplete:Function, onFailure:Function, progress:Function, mover:Function;

        public function AssetPublication(stage:File, root:File, names:Array,
                complete:Function, failed:Function, progress:Function = null,
                timeoutMs:uint = 30000, move:Function = null)
        {
            this.stage = stage; this.root = root; this.names = names.concat();
            this.onComplete = complete; this.onFailure = failed; this.progress = progress; this.mover = move;
            timer = new Timer(Math.max(1, timeoutMs), 1);
            timer.addEventListener(TimerEvent.TIMER_COMPLETE, timedOut);
        }

        public function start():void
        {
            if (started || finished) return;
            started = true;
            try
            {
                if (!names.length || !stage || !root || !stage.isDirectory || !root.isDirectory || stage.parent.nativePath != root.nativePath || stage.name.indexOf(".ob-stage-") != 0)
                    throw new Error("Invalid asset publication directories.");
                var seen:Object = {};
                for each (var item:String in names)
                {
                    if (!item || item == "." || item == ".." || /[\\\/:]/.test(item) || seen[item.toLowerCase()])
                        throw new Error("Invalid or duplicate asset publication name.");
                    seen[item.toLowerCase()] = true;
                    if (!stage.resolvePath(item).exists) throw new Error("Missing staged asset: " + item);
                }
                backup = root.resolvePath(".ob-backup-" + new Date().time.toString(16) + "-" + uint(Math.random() * uint.MAX_VALUE).toString(16));
                if (backup.exists) throw new Error("Asset backup directory already exists.");
                total = names.length * 2;
                advance();
            }
            catch (error:Error) { fail(error.message); }
        }

        private function advance():void
        {
            if (finished) return;
            try
            {
                if (rollingBack)
                {
                    if (!recovery.length) { finishFailure(failure + " The previous generation was restored."); return; }
                    var operation:Object = recovery.shift();
                    begin(operation.from, operation.to, "rollback", operation.name);
                    return;
                }
                while (at < total)
                {
                    var index:int = at % names.length;
                    var item:String = names[index];
                    if (at < names.length)
                    {
                        if (!root.resolvePath(item).exists) { at++; moves++; report("Retaining previous assets: " + item); continue; }
                        backup.createDirectory();
                        begin(root.resolvePath(item), backup.resolvePath(item), "backup", item);
                    }
                    else begin(stage.resolvePath(item), root.resolvePath(item), "install", item);
                    return;
                }
                dispose(); finished = true;
                var complete:Function = onComplete; clearCallbacks();
                if (complete != null) complete(saved.length ? backup : null);
            }
            catch (error:Error) { fail(error.message); }
        }

        private function begin(from:File, to:File, kind:String, name:String):void
        {
            this.kind = kind; this.name = name;
            original = new File(from.nativePath); destination = new File(to.nativePath); active = from;
            if (!original.exists || destination.exists) throw new Error("Publication path changed: " + from.nativePath);
            active.addEventListener(Event.COMPLETE, completed);
            active.addEventListener(IOErrorEvent.IO_ERROR, ioError);
            active.addEventListener(SecurityErrorEvent.SECURITY_ERROR, securityError);
            active.addEventListener(Event.CANCEL, cancelled);
            report((rollingBack ? "Recovering assets: " : kind == "backup" ? "Retaining previous assets: " : "Publishing assets: ") + name);
            timer.reset(); timer.start();
            if (mover != null) mover(from, to); else from.moveToAsync(to, false);
        }

        private function completed(event:Event):void
        {
            if (event.currentTarget != active || finished) return;
            if (original.exists || !destination.exists) { fail("Asset move did not complete: " + original.nativePath); return; }
            rememberMove(); detach();
            if (!rollingBack) { at++; moves++; }
            advance();
        }
        private function rememberMove():void
        {
            if (kind == "backup") saved.push(name);
            else if (kind == "install") installed.push(name);
        }
        private function ioError(event:IOErrorEvent):void { if (event.currentTarget == active) fail(event.text); }
        private function securityError(event:SecurityErrorEvent):void { if (event.currentTarget == active) fail(event.text); }
        private function cancelled(event:Event):void { if (event.currentTarget == active) fail("Asset publication was cancelled."); }
        private function timedOut(event:TimerEvent):void { fail("Asset publication timed out while moving " + name + ". Check locks, disk space and permissions."); }

        private function fail(message:String):void
        {
            if (finished) return;
            if (active)
            {
                var pending:File = active;
                // Detach first: cancel may synchronously dispatch Event.CANCEL.
                detach();
                try { pending.cancel(); } catch (ignored:Error) { }
                // A rename may have completed immediately before cancellation.
                if (!original.exists && destination.exists) rememberMove();
                else if (!original.exists || destination.exists)
                { finishFailure(message + " Ambiguous move state; recovery files were retained."); return; }
            }
            if (rollingBack) { finishFailure(message + " Rollback did not finish; recovery files were retained."); return; }
            failure = message; rollingBack = true; recovery = [];
            for (var i:int = installed.length - 1; i >= 0; i--)
                recovery.push({from:root.resolvePath(installed[i]), to:stage.resolvePath(installed[i]), name:installed[i]});
            for (i = saved.length - 1; i >= 0; i--)
                recovery.push({from:backup.resolvePath(saved[i]), to:root.resolvePath(saved[i]), name:saved[i]});
            advance();
        }
        private function report(label:String):void { if (progress != null) progress(moves, Math.max(1, total), label); }
        private function finishFailure(message:String):void
        {
            dispose(); finished = true;
            var failed:Function = onFailure; clearCallbacks();
            if (failed != null) failed(new Error(message + (backup ? " Backup: " + backup.nativePath : "") + (stage ? " Staging: " + stage.nativePath : "")));
        }
        private function detach():void
        {
            timer.reset();
            if (!active) return;
            active.removeEventListener(Event.COMPLETE, completed);
            active.removeEventListener(IOErrorEvent.IO_ERROR, ioError);
            active.removeEventListener(SecurityErrorEvent.SECURITY_ERROR, securityError);
            active.removeEventListener(Event.CANCEL, cancelled);
            active = null;
        }
        private function dispose():void { detach(); timer.removeEventListener(TimerEvent.TIMER_COMPLETE, timedOut); }
        private function clearCallbacks():void { onComplete = onFailure = progress = mover = null; }
    }
}
