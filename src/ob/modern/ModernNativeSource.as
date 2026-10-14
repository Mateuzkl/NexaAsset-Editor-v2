package ob.modern
{
    import flash.events.Event;
    import flash.events.EventDispatcher;
    import flash.filesystem.File;
    import flash.filesystem.FileStream;
    import flash.filesystem.FileMode;
    import flash.utils.ByteArray;
    import flash.utils.Endian;
    import flash.utils.setTimeout;
    import otlib.animation.FrameGroup;
    import otlib.animation.FrameDuration;
    import otlib.things.ThingType;
    import otlib.utils.ThingListItem;
    import otlib.sprites.SpriteData;
    public class ModernNativeSource extends EventDispatcher
    {
        public var session:ModernAssetSession=new ModernAssetSession();
        public var directory:File;
        public var inventory:Object;
        public var query:String="";
        public var hideEmpty:Boolean=false;
        public var revision:Number=0;
        private var uniqueJob:uint=0;
        public function request(command:String,fields:Object,callback:Function=null):void
        {enqueue("operation:"+(++uniqueJob),command,fields,callback || function(result:Object):void {});}
        private var queue:Array=[];
        private var active:Object;
        private var thingPage:Vector.<ThingListItem>=new Vector.<ThingListItem>();
        private var spritePage:Vector.<SpriteData>=new Vector.<SpriteData>();
        private var selectedData:ModernThingData;
        private var revisions:Object={};
        public function ModernNativeSource(){session.addEventListener(ModernAssetEvent.MESSAGE,message);}
        public function get ready():Boolean{return session.ready;}
        public function get busy():Boolean{return active != null || queue.length > 0 || session.busy;}
        public function open(folder:File,helper:File=null):void
        {
            directory=folder;
            session.open(folder,helper||File.applicationDirectory.resolvePath("modern-helper/nexa-modern-helper.exe"),new File(File.applicationDirectory.nativePath).parent.resolvePath("nexa-modern-sessions"));
        }
        public function enqueue(key:String,command:String,fields:Object,callback:Function,onError:Function=null):void
        {
            if(active && active.key==key && JSON.stringify(active.fields)==JSON.stringify(fields))return;
            revisions[key]=uint(revisions[key])+1;
            for(var i:int=queue.length-1;i>=0;i--)if(queue[i].key==key)queue.splice(i,1);
            if(queue.length>=16){var removed:Boolean=false;for(var drop:int=queue.length-1;drop>=0;drop--)if(queue[drop].command=="native-frame"){delete revisions[queue[drop].key];queue.splice(drop,1);removed=true;break;}if(!removed){delete revisions[key];throw new Error("Native request queue is full. Wait for the current operation to finish.");}}
            var job:Object={key:key,command:command,fields:fields,callback:callback,onError:onError,revision:revisions[key]};
            if(command=="native-frame")queue.push(job);
            else if(command=="native-page" || command=="native-detail" || command=="native-sprites")queue.unshift(job);
            else {var slot:int=0;while(slot<queue.length && queue[slot].command!="native-frame")slot++;queue.splice(slot,0,job);} // Never reverse the user's edit order.
            pump();
        }
        private function pump():void
        {if(!ready || active || session.busy || !queue.length)return;active=queue.shift();try{session.request(active.command,active.fields);}catch(error:Error){var failed:Object=active;active=null;if(failed && revisions[failed.key]==failed.revision)delete revisions[failed.key];if(failed && failed.onError!=null)failed.onError();dispatchEvent(new ModernAssetEvent({type:"error",message:error.message}));pump();}}
        private function message(event:ModernAssetEvent):void
        {
            var m:Object=event.message;
            if(m.type=="ready"){inventory=m.native;dispatchEvent(event.clone());pump();return;}
            if(m.type=="result"){
                var job:Object=active;active=null;
                revision=m.revision || 0;
                if(m.native){inventory=m.native;dispatchEvent(new ModernAssetEvent({type:"inventory",revision:revision}));}
                try{if(job && job.revision==revisions[job.key])job.callback(m.result);}catch(error:Error){if(job && job.onError!=null)job.onError();dispatchEvent(new ModernAssetEvent({type:"error",message:error.message}));}
                if(job && revisions[job.key]==job.revision)delete revisions[job.key];
                pump();return;
            }
            if(m.type=="error"){var failedJob:Object=active;active=null;m.command=failedJob ? failedJob.command : null;if(failedJob && failedJob.onError!=null)failedJob.onError();if(failedJob && revisions[failedJob.key]==failedJob.revision)delete revisions[failedJob.key];if(!ready){queue=[];revisions={};}}
            dispatchEvent(event.clone());if(m.type=="error")pump();
        }
        public function objects(target:uint,category:String,callback:Function):void
        {
            enqueue("objects","native-page",{category:category,target:target,query:query,hideEmpty:hideEmpty},function(result:Object):void {
                var records:Array=readPixels(result.file,"native-things.bin"),pixels:Object={};for each(var r:Object in records)pixels[r.id]=r;
                var list:Vector.<ThingListItem>=new Vector.<ThingListItem>();
                for each(var model:Object in result.items){var item:ThingListItem=new ThingListItem();item.thing=modelThing(model);item.frameGroup=item.thing.frameGroups[0].clone();item.frameGroup.width=pixels[model.id].width/32;item.frameGroup.height=pixels[model.id].height/32;item.pixels=pixels[model.id].pixels;list.push(item);}
                if(callback(result,list)===false){for each(var rejected:ThingListItem in list)rejected.dispose();return;}
                var old:Vector.<ThingListItem>=thingPage;thingPage=list;
                setTimeout(function():void{for each(var item:ThingListItem in old)item.dispose();},100);
            });
        }
        public function sprites(target:uint,callback:Function):void
        {
            enqueue("sprites","native-sprites",{target:target},function(result:Object):void {
                var list:Vector.<SpriteData>=new Vector.<SpriteData>();
                for each(var r:Object in readPixels(result.file,"native-sprites.bin")){var sprite:ModernSpriteData=new ModernSpriteData();sprite.id=r.id;sprite.imageWidth=r.width;sprite.imageHeight=r.height;sprite.pixels=r.pixels;list.push(sprite);}
                var old:Vector.<SpriteData>=spritePage;spritePage=list;callback(result,list);
                setTimeout(function():void{for each(var sprite:SpriteData in old)sprite.dispose();},100);
            });
        }
        public function detail(id:uint,category:String,callback:Function):void
        {
            var thisSource:ModernNativeSource=this;
            enqueue("detail","native-detail",{category:category,appearanceId:id},function(result:Object):void {
                var data:ModernThingData=new ModernThingData();data.thing=modelThing(result);data.clientVersion=1525;data.source=thisSource;data.flags=result.flags;data.flagRows=result.flagRows;data.metadataRows=result.metadataRows;
                for each(var group:Object in result.groups){data.groupIndices[group.type]=group.index;data.groupLabels[group.type]="Group "+group.index+" · native type "+group.originalType;}
                if(data.groupIndices[0]==null)data.groupIndices[0]=data.groupIndices[1];
                if(callback(data)===false){data.dispose();return;}
                var old:ModernThingData=selectedData;selectedData=data;
                if(old)setTimeout(old.dispose,100);
            });
        }
        private function modelThing(model:Object):ThingType
        {
            var thing:ThingType=new ThingType();thing.id=model.id;thing.category=model.category;thing.name=model.name;
            if(model.properties)for(var key:String in model.properties)if(key in thing)thing[key]=model.properties[key];
            for each(var source:Object in model.groups){
                var g:FrameGroup=new FrameGroup();g.type=source.type;g.width=source.width;g.height=source.height;g.exactSize=source.exactSize;g.layers=source.layers;g.patternX=source.patternX;g.patternY=source.patternY;g.patternZ=source.patternZ;g.frames=source.frames;g.isAnimation=g.frames>1;g.animationMode=source.mode;g.loopCount=source.loops;g.startFrame=source.start;g.spriteIndex=new Vector.<uint>();g.frameDurations=new Vector.<FrameDuration>();
                for(var i:int=0;i<g.frames;i++){var phase:Object=source.phases.length>i?source.phases[i]:null;g.frameDurations.push(new FrameDuration(phase && phase.min!=null?phase.min:100,phase && phase.max!=null?phase.max:100));}
                thing.frameGroups[g.type]=g;
            }
            if(!thing.frameGroups[0] && thing.frameGroups[1]){thing.frameGroups[0]=thing.frameGroups[1].clone();thing.frameGroups[0].type=0;}
            return thing;
        }
        public function readPixels(path:String,name:String):Array
        {
            var file:File=new File(path);if(!session.directory || file.nativePath!=session.directory.resolvePath(name).nativePath)throw new Error("Native pixels outside owned session.");
            var stream:FileStream=new FileStream();var rows:Array=[];
            try{stream.open(file,FileMode.READ);stream.endian=Endian.LITTLE_ENDIAN;if(stream.bytesAvailable>16777216 || stream.bytesAvailable<8 || stream.readUnsignedInt()!=0x3142584E)throw new Error("Invalid native pixel packet.");
                var count:uint=stream.readUnsignedInt();if(count>100)throw new Error("Native page exceeds 100 records.");
                for(var i:uint=0;i<count;i++){if(stream.bytesAvailable<16)throw new Error("Truncated native record.");var id:uint=stream.readUnsignedInt(),w:int=stream.readInt(),h:int=stream.readInt(),length:int=stream.readInt();if(w<1 || h<1 || w>8192 || h>8192 || Number(w)*h*4!=length || length>8388608 || length>stream.bytesAvailable)throw new Error("Invalid native dimensions/payload.");var pixels:ByteArray=new ByteArray();stream.readBytes(pixels,0,length);rows.push({id:id,width:w,height:h,pixels:pixels});}
                if(stream.bytesAvailable)throw new Error("Trailing native pixel data.");
            }finally{stream.close();}return rows;
        }
        public function close():void
        {session.removeEventListener(ModernAssetEvent.MESSAGE,message);session.close();queue=[];revisions={};active=null;for each(var item:ThingListItem in thingPage)item.dispose();for each(var sprite:SpriteData in spritePage)sprite.dispose();thingPage=new Vector.<ThingListItem>();spritePage=new Vector.<SpriteData>();if(selectedData)selectedData.dispose();selectedData=null;}
    }
}
