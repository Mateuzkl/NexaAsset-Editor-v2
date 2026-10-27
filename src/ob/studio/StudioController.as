package ob.studio {
    import flash.desktop.NativeProcess;
    import flash.desktop.NativeProcessStartupInfo;
    import flash.events.*;
    import flash.filesystem.File;
    import flash.utils.*;
    import flash.desktop.Clipboard;
    import flash.desktop.ClipboardFormats;
    import mx.utils.UIDUtil;
    public class StudioController extends EventDispatcher {
        public var editor:Object;
        public var endpoint:String="";
        private var token:String="";
        public var ready:Boolean=false;
        public var status:String="Studio stopped";
        public var enabled:Boolean=false;
        private var process:NativeProcess;
        private var output:String="";
        private var pending:Object={};
        private var sequence:uint=0;
        private var startup:uint=0;
        private var drafts:Object={};
        public function get hasUncommittedDrafts():Boolean {for each(var open:Boolean in drafts)if(open)return true;return false;}
        public function start(executable:File=null):void {
            if(process)return;
            var info:NativeProcessStartupInfo=new NativeProcessStartupInfo();info.executable=executable||File.applicationDirectory.resolvePath("mcp-gateway/nexa-mcp-gateway.exe");
            if(!info.executable.exists)throw new Error("MCP gateway is missing. Build the portable application first.");info.workingDirectory=info.executable.parent;
            process=new NativeProcess();process.addEventListener(ProgressEvent.STANDARD_OUTPUT_DATA,stdout);process.addEventListener(ProgressEvent.STANDARD_ERROR_DATA,stderr);process.addEventListener(NativeProcessExitEvent.EXIT,exited);process.addEventListener(IOErrorEvent.STANDARD_INPUT_IO_ERROR,ioError);
            try{process.start(info);startup=setTimeout(function():void{startup=0;fail("Studio gateway startup timed out.");},30000);}catch(error:Error){stop();throw error;}
        }
        public function call(method:String,args:Object,callback:Function=null):void {
            if(!ready){if(callback!=null)callback(null,"Studio is not ready.");return;}
            var id:String=String(++sequence);if(sequence>uint.MAX_VALUE-1)sequence=0;
            var timer:uint=setTimeout(function():void{var request:Object=pending[id];delete pending[id];if(request&&request.callback!=null)request.callback(null,"Studio request timed out. Refresh before retrying.");},30000);
            pending[id]={callback:callback,timer:timer};
            try{send({type:"uiRequest",id:id,method:method,args:args});}catch(error:Error){clearTimeout(timer);delete pending[id];if(callback!=null)callback(null,error.message);}
        }
        private function send(message:Object):void {if(!process||!process.running)throw new Error("Studio stopped.");var payload:String=JSON.stringify(message);if(payload.length>4194304)throw new Error("Studio payload exceeds 4 MiB.");process.standardInput.writeUTFBytes(payload+"\n");}
        private function stdout(event:ProgressEvent):void {
            try{
                while(process&&process.standardOutput.bytesAvailable){output+=process.standardOutput.readUTFBytes(Math.min(32768,process.standardOutput.bytesAvailable));if(output.length>4194304)throw new Error("Studio response exceeds 4 MiB.");
                    var at:int;while((at=output.indexOf("\n"))>=0){var line:String=output.substr(0,at);output=output.substr(at+1);if(line.length)accept(JSON.parse(line));if(!process)return;}
                }
            }catch(error:Error){fail("Studio protocol error: "+error.message);}
        }
        private function accept(m:Object):void {
            if(m.type=="ready"){if(m.protocolVersion!=1)throw new Error("Incompatible Studio protocol.");if(startup)clearTimeout(startup);startup=0;ready=true;endpoint=m.endpoint;token=m.token;status="MCP disabled · manual Studio ready";dispatchEvent(new Event("ready"));dispatchEvent(new Event("status"));}
            else if(m.type=="uiResponse"){
                var request:Object=pending[m.id];delete pending[m.id];if(!request)return;clearTimeout(request.timer);if(request.callback!=null)request.callback(m.result,m.error);
            }else if(m.type=="changed"){if(m.closed)delete drafts[m.editId];else drafts[m.editId]=!m.committed;dispatchEvent(new StudioEvent(m));}
            else if(m.type=="activity"){status=(enabled?"MCP enabled":"MCP disabled")+" · "+m.action+" · "+m.state+" · "+endpoint;dispatchEvent(new Event("status"));}
            else if(m.type=="editorRequest"){
                var ownProcess:NativeProcess=process;
                try{editor.studioEditorRequest(m.method,m.args,function(result:Object,error:String=null):void{if(process!=ownProcess)return;send(error?{type:"editorResponse",id:m.id,error:error}:{type:"editorResponse",id:m.id,result:result});});}
                catch(error:Error){send({type:"editorResponse",id:m.id,error:error.message});}
            }
        }
        public function setEnabled(value:Boolean):void {call("ui_enable",{enabled:value},function(result:Object,error:String):void{if(error)status=error;else{enabled=value;status=(enabled?"MCP enabled · ":"MCP disabled · ")+endpoint;}dispatchEvent(new Event("status"));});}
        /** Closing the window preserves drafts but disables access, revokes provider consent and rotates the bearer. */
        public function suspend():void {if(!ready)return;call("ui_suspend",{},function(result:Object,error:String):void {enabled=false;if(!error)token=result.token;status=error||"MCP paused · reopen Pixel Art Studio to enable";dispatchEvent(new Event("status"));});}
        public function copyConfiguration():void {Clipboard.generalClipboard.setData(ClipboardFormats.TEXT_FORMAT,"[mcp_servers.nexaasset]\nurl = \""+endpoint+"\"\nbearer_token_env_var = \"NEXA_MCP_TOKEN\"\n");}
        public function copyToken():void {if(token.length)Clipboard.generalClipboard.setData(ClipboardFormats.TEXT_FORMAT,token);}
        private function stderr(event:ProgressEvent):void {if(process)process.standardError.readUTFBytes(process.standardError.bytesAvailable);}
        private function ioError(event:IOErrorEvent):void {fail("Studio process pipe failed.");}
        private function exited(event:NativeProcessExitEvent):void {fail("Studio gateway exited. Reopen the Studio to reconnect.");}
        private function fail(message:String):void {stop();status=message;dispatchEvent(new Event("status"));}
        public function stop():void {
            if(startup)clearTimeout(startup);startup=0;
            var old:NativeProcess=process;process=null;ready=false;enabled=false;token="";endpoint="";output="";drafts={};
            if(old){old.removeEventListener(ProgressEvent.STANDARD_OUTPUT_DATA,stdout);old.removeEventListener(ProgressEvent.STANDARD_ERROR_DATA,stderr);old.removeEventListener(NativeProcessExitEvent.EXIT,exited);old.removeEventListener(IOErrorEvent.STANDARD_INPUT_IO_ERROR,ioError);if(old.running)try{old.exit(true);}catch(ignore:Error){}}
            var requests:Object=pending;pending={};for each(var request:Object in requests){clearTimeout(request.timer);if(request.callback!=null)request.callback(null,"Studio stopped; drafts were invalidated.");}
        }
    }
}
