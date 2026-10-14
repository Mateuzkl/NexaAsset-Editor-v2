package ob.modern
{
    import flash.events.EventDispatcher;
    import flash.filesystem.File;
    import flash.utils.Timer;
    import flash.events.TimerEvent;

    /** Worker exports strict OBD packets; the existing bounded helper publishes a new native project. */
    public final class LegacyModernProjectExport extends EventDispatcher
    {
        public var directory:File;
        public var target:File;
        public var job:uint;
        private var count:int;
        private var parent:File;
        private var session:ModernAssetSession;
        private var workerTimer:Timer=new Timer(120000,1);
        public function LegacyModernProjectExport(target:File,count:int,job:uint) {
            if(count<1||count>100)throw new Error("Select 1–100 objects per modern project export.");
            if(target.exists||!target.parent.isDirectory)throw new Error("Choose a new modern project folder; existing files are preserved.");
            this.target=target;this.count=count;this.job=job;
            parent=new File(File.applicationDirectory.nativePath).parent.resolvePath("nexa-modern-sessions");parent.createDirectory();
            directory=parent.resolvePath("legacy-export-"+new Date().time+"-"+uint(Math.random()*uint.MAX_VALUE));
            if(directory.exists)throw new Error("Temporary export collision.");directory.createDirectory();
            workerTimer.addEventListener(TimerEvent.TIMER_COMPLETE,timedOut);workerTimer.start();
        }
        private function timedOut(event:TimerEvent):void {dispatchEvent(new ModernAssetEvent({type:"error",message:"Legacy export worker timed out."}));}
        public function workerFinished(success:Boolean,message:String):void {
            workerTimer.stop();if(!success){dispatchEvent(new ModernAssetEvent({type:"error",message:message}));return;}
            session=new ModernAssetSession();session.addEventListener(ModernAssetEvent.MESSAGE,received);
            try{session.open(directory,new File(File.applicationDirectory.nativePath).resolvePath("modern-helper/nexa-modern-helper.exe"),parent,true);}
            catch(error:Error){dispatchEvent(new ModernAssetEvent({type:"error",message:error.message}));}
        }
        private function received(event:ModernAssetEvent):void {
            var message:Object=event.message;
            if(message.type=="ready") {
                if(message.notices && message.notices.length)dispatchEvent(new ModernAssetEvent({type:"notice",message:message.notices.join("\n")}));
                try{session.request("native-save-as",{target:target.nativePath});}catch(error:Error){dispatchEvent(new ModernAssetEvent({type:"error",message:error.message}));}
            } else if(message.type=="result")dispatchEvent(new ModernAssetEvent({type:"complete",directory:target.nativePath}));
            else dispatchEvent(event.clone());
        }
        public function close():void {
            workerTimer.stop();workerTimer.removeEventListener(TimerEvent.TIMER_COMPLETE,timedOut);
            if(session){session.removeEventListener(ModernAssetEvent.MESSAGE,received);session.close();session=null;}
            if(directory && directory.parent.nativePath==parent.nativePath && directory.name.indexOf("legacy-export-")==0) {
                for(var i:int=0;i<count;i++)removeOwned("object-"+i+".obd");removeOwned("manifest.json");
                try{directory.deleteDirectory(false);}catch(error:Error){}
            }
        }
        private function removeOwned(name:String):void {try{var file:File=directory.resolvePath(name);if(file.exists&&!file.isDirectory)file.deleteFile();}catch(error:Error){}}
    }
}
