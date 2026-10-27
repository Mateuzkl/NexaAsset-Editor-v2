package ob.studio {
    import flash.display.Bitmap;
    import flash.display.BitmapData;
    import flash.events.Event;
    import flash.events.MouseEvent;
    import flash.text.TextField;
    import flash.text.TextFormat;
    import flash.utils.ByteArray;
    import mx.core.UIComponent;

    /** Read-only atlas review. Clicking a cell opens its exact time/direction for painting. */
    [Event(name="cellSelected",type="flash.events.Event")]
    public class PixelSheet extends UIComponent {
        private var bitmaps:Array=[];
        private var columns:int=1, rows:int=1, cellWidth:int=32, cellHeight:int=32, count:int=0;
        private var pitchX:int=104, pitchY:int=120;
        private var ownId:String="";
        private var revision:Number=-1;
        private var highlight:UIComponent=new UIComponent();
        private var chosen:int=0;
        public var zoom:int=3;
        public var viewportWidth:Number=760,viewportHeight:Number=400;
        public var directionLabels:Array=["N","E","S","W"];
        public function PixelSheet() {super();addEventListener(MouseEvent.CLICK,clicked);highlight.mouseEnabled=false;highlight.mouseChildren=false;}
        public function get selectedCell():int {return chosen;}
        public function set selectedCell(value:int):void {chosen=value;drawSelection();}
        public function matches(id:String, version:Number):Boolean {return ownId==id&&revision==version;}
        public function load(id:String, version:Number, sheet:Object):void {
            if(id==ownId&&version==revision)return;
            clear();ownId=id;revision=version;columns=sheet.columns;rows=sheet.rows;count=sheet.cells.length;cellWidth=sheet.cellWidth;cellHeight=sheet.cellHeight;
            zoom=Math.max(1,Math.min(3,Math.floor((viewportWidth-columns*12)/(columns*cellWidth)),Math.floor((viewportHeight-24-rows*20)/(rows*cellHeight))));
            pitchX=Math.max(78,cellWidth*zoom+12);pitchY=cellHeight*zoom+20;
            width=columns*pitchX;height=24+rows*pitchY;
            for(var col:int=0;col<columns;col++)label(sheet.directions==4?String(directionLabels[col]):String(col+1),col*pitchX,2);
            for(var i:int=0;i<count;i++) {
                var raw:ByteArray=StudioPixels.decode(sheet.cells[i].rgba),surface:BitmapData=new BitmapData(cellWidth,cellHeight,true,0);
                try {for(var at:int=0;at<raw.length;at+=4)surface.setPixel32(int(at/4)%cellWidth,int(at/4/cellWidth),(uint(raw[at+3])<<24)|(uint(raw[at])<<16)|(uint(raw[at+1])<<8)|uint(raw[at+2]));}
                finally {raw.clear();}
                bitmaps.push(surface);var view:Bitmap=new Bitmap(surface);view.smoothing=false;view.scaleX=view.scaleY=zoom;view.x=int(i%columns)*pitchX;view.y=24+int(i/columns)*pitchY;
                for(var y:int=0;y<cellHeight;y+=4)for(var x:int=0;x<cellWidth;x+=4){graphics.beginFill(((x/4+y/4)%2)?0x263746:0x172633);graphics.drawRect(view.x+x*zoom,view.y+y*zoom,4*zoom,4*zoom);graphics.endFill();}
                addChild(view);label("T"+(sheet.cells[i].time+1)+" · "+sheet.cells[i].duration+"ms",view.x,view.y+cellHeight*zoom+1);
            }
            addChild(highlight);drawSelection();invalidateDisplayList();
        }
        private function label(value:String,x:int,y:int):void {var text:TextField=new TextField();text.defaultTextFormat=new TextFormat("Segoe UI",11,0xADC0D4);text.text=value;text.width=pitchX;text.height=18;text.x=x;text.y=y;text.selectable=false;text.mouseEnabled=false;addChild(text);}
        /** Ignore gutters and labels instead of selecting a neighbouring cell. */
        public function cellAt(x:Number,y:Number):int {
            y-=24;if(x<0||y<0)return -1;
            var col:int=int(x/pitchX),row:int=int(y/pitchY),index:int=row*columns+col;
            return col<columns&&row<rows&&index<count&&x%pitchX<cellWidth*zoom&&y%pitchY<cellHeight*zoom?index:-1;
        }
        private function clicked(event:MouseEvent):void {if(!enabled)return;var index:int=cellAt(mouseX,mouseY);if(index<0)return;selectedCell=index;dispatchEvent(new Event("cellSelected"));}
        private function drawSelection():void {highlight.graphics.clear();if(chosen<0||chosen>=count)return;highlight.graphics.lineStyle(2,0x24C8FF);highlight.graphics.drawRect(chosen%columns*pitchX,24+int(chosen/columns)*pitchY,cellWidth*zoom,cellHeight*zoom);}
        private function clear():void {for each(var surface:BitmapData in bitmaps)surface.dispose();bitmaps=[];while(numChildren)removeChildAt(0);graphics.clear();}
        public function dispose():void {clear();removeEventListener(MouseEvent.CLICK,clicked);ownId="";revision=-1;count=0;}
    }
}
