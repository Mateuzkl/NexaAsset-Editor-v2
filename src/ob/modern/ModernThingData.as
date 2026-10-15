package ob.modern
{
    import flash.display.BitmapData;
    import flash.events.Event;
    import flash.events.EventDispatcher;
    import otlib.things.ThingData;
    import otlib.animation.FrameGroup;
    public class ModernThingData extends ThingData
    {
        public var source:ModernNativeSource;
        public var events:EventDispatcher=new EventDispatcher();
        public var flags:String;
        [Bindable] public var flagRows:Array=[];
        [Bindable] public var metadataRows:Array=[];
        public var groupIndices:Object={};
        public var groupLabels:Object={};
        private var images:Object={};
        private var failedImages:Object={};
        private var order:Array=[];
        private var bytes:Number=0;
        private var disposed:Boolean=false;
        public function image(groupType:uint,frame:int,x:int=0,y:int=0,z:int=0,layer:int=-1,grid:Boolean=false):BitmapData
        {
            if (disposed || !source || !source.ready) return null;
            var group:FrameGroup=thing.getFrameGroup(groupType);if (!group) return null;
            frame=Math.max(0,frame)%group.frames;x=Math.max(0,x)%group.patternX;y=Math.max(0,y)%group.patternY;z=Math.max(0,z)%group.patternZ;
            var key:String=[groupType,frame,x,y,z,layer,grid].join(":");
            if (failedImages[key]) return null;
            if (images[key]) { var at:int=order.indexOf(key);if(at>=0)order.splice(at,1);order.push(key);return images[key]; }
            source.enqueue("frame:"+category+":"+id+":"+key,"native-frame",{category:category,appearanceId:id,group:groupIndices[groupType],frame:frame,patternX:x,patternY:y,patternZ:z,layer:layer,grid:grid},function(result:Object):void {
                if(disposed)return;
                var records:Array=source.readPixels(result.file,"native-frame.bin");var record:Object=records[0];
                var bitmap:BitmapData=new BitmapData(record.width,record.height,true,0);record.pixels.position=0;bitmap.setPixels(bitmap.rect,record.pixels);record.pixels.clear();
                if(images[key]) {bitmap.dispose();return;}
                var size:Number=bitmap.width*bitmap.height*4;
                while(order.length && (bytes+size>8388608 || order.length>=64)) {var old:String=order.shift();var previous:BitmapData=images[old];bytes-=previous.width*previous.height*4;previous.dispose();delete images[old];}
                images[key]=bitmap;order.push(key);bytes+=size;events.dispatchEvent(new Event(Event.CHANGE));
            },function():void {if(!disposed)failedImages[key]=true;});return null;
        }
        public override function getBitmap(group:FrameGroup,layer:uint=0,x:uint=0,y:uint=0,z:uint=0,frame:uint=0):BitmapData
        {var bitmap:BitmapData=image(group.type,frame,x,y,z,layer);return bitmap ? bitmap.clone() : null;}
        public function dispose():void
        {disposed=true;for each(var bitmap:BitmapData in images)bitmap.dispose();images={};failedImages={};order=[];bytes=0;source=null;}
        public function get cacheBytes():Number{return bytes;}
    }
}
