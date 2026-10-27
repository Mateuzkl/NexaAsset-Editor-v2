package ob.studio {
    import mx.core.UIComponent;
    import flash.display.Bitmap;
    import flash.display.BitmapData;
    import flash.events.*;
    import flash.geom.Rectangle;
    import flash.utils.ByteArray;
    import flash.ui.Keyboard;

    /** Editable straight RGBA layer. BitmapData is a disposable display surface only. */
    [Event(name="stroke",type="flash.events.Event")]
    [Event(name="picked",type="flash.events.Event")]
    public class PixelCanvas extends UIComponent {
        [Bindable] public var coordinate:String="";
        public var tool:String="pencil";
        public var color:uint=0x80C8FFFF; // RRGGBBAA
        public var brushSize:int=1;
        public var grid:Boolean=true;
        public var onion:Boolean=false;
        public var previous:ByteArray;
        public var layer:int=0;
        public var documentRevision:Number=0;
        public var drawing:Boolean=false;
        public var pixels:ByteArray;
        public var composed:ByteArray;
        public var imageWidth:int=32;
        public var imageHeight:int=32;
        public var zoom:int=8;
        public var layers:Array=[];
        public var selection:Rectangle;
        private var display:Bitmap=new Bitmap();
        private var surface:BitmapData;
        private var dragging:Boolean=false;
        private var pointerStage:flash.display.Stage;
        private var sx:int,sy:int,lx:int,ly:int;
        private var copied:ByteArray;
        private var copyW:int,copyH:int;
        public function PixelCanvas(){super();addChild(display);tabEnabled=true;addEventListener(MouseEvent.MOUSE_DOWN,down);addEventListener(MouseEvent.MOUSE_MOVE,pointerMove);addEventListener(KeyboardEvent.KEY_DOWN,key);addEventListener(Event.REMOVED_FROM_STAGE,removed);}
        public function load(snapshot:Object,selected:int):void {
            if(dragging)return;
            imageWidth=snapshot.document.width;imageHeight=snapshot.document.height;documentRevision=snapshot.document.documentRevision;
            layers=snapshot.layers;layer=Math.max(0,Math.min(selected,layers.length-1));
            if(pixels)pixels.clear();pixels=StudioPixels.decode(layers[layer].rgba);draw();
        }
        public function setZoom(value:int):void {zoom=Math.max(1,Math.min(16,value));draw();}
        public function draw():void {
            width=imageWidth*zoom;height=imageHeight*zoom;
            if(!surface||surface.width!=imageWidth||surface.height!=imageHeight){if(surface)surface.dispose();surface=new BitmapData(imageWidth,imageHeight,true,0);display.bitmapData=surface;}
            if(composed)composed.clear();composed=new ByteArray();composed.length=imageWidth*imageHeight*4;
            var all:Array=[];for(var i:int=0;i<layers.length;i++)all.push(i==layer?pixels:StudioPixels.decode(layers[i].rgba));
            for(var at:int=0;at<composed.length;at+=4){var r:int=0,g:int=0,b:int=0,a:int=0,hasBase:Boolean=false;
                for(i=0;i<all.length;i++){if(!layers[i].visible||int(layers[i].opacity)==0)continue;var raw:ByteArray=all[i],sa:int=(raw[at+3]*int(layers[i].opacity)+127)/255;
                    if(sa==0){if(!hasBase){r=raw[at];g=raw[at+1];b=raw[at+2];}hasBase=true;continue;}hasBase=true;var n:int=sa*255+a*(255-sa);
                    r=(raw[at]*sa*255+r*a*(255-sa)+int(n/2))/n;g=(raw[at+1]*sa*255+g*a*(255-sa)+int(n/2))/n;b=(raw[at+2]*sa*255+b*a*(255-sa)+int(n/2))/n;a=(n+127)/255;
                }composed[at]=r;composed[at+1]=g;composed[at+2]=b;composed[at+3]=a;
                var x:int=(at/4)%imageWidth,y:int=int(at/4/imageWidth);
                if(onion&&previous&&previous.length==composed.length&&a==0){r=previous[at];g=previous[at+1];b=previous[at+2];a=int(previous[at+3]/3);}
                surface.setPixel32(x,y,(uint(a)<<24)|(uint(r)<<16)|(uint(g)<<8)|uint(b));
            }
            for(i=0;i<all.length;i++)if(i!=layer)ByteArray(all[i]).clear();
            display.smoothing=false;display.scaleX=display.scaleY=zoom;
            graphics.clear();for(y=0;y<imageHeight;y+=4)for(x=0;x<imageWidth;x+=4){graphics.beginFill(((x/4+y/4)%2)?0x263746:0x172633);graphics.drawRect(x*zoom,y*zoom,4*zoom,4*zoom);graphics.endFill();}
            // Graphics under the bitmap provide transparency checks; grid/selection use a top overlay.
            overlay();invalidateDisplayList();
        }
        private var guides:UIComponent;
        private function overlay():void {
            if(!guides){guides=new UIComponent();guides.mouseEnabled=false;guides.mouseChildren=false;addChild(guides);}guides.graphics.clear();
            if(grid&&zoom>=5){guides.graphics.lineStyle(1,0x718498,0.25);for(var x:int=0;x<=imageWidth;x++){guides.graphics.moveTo(x*zoom,0);guides.graphics.lineTo(x*zoom,height);}for(var y:int=0;y<=imageHeight;y++){guides.graphics.moveTo(0,y*zoom);guides.graphics.lineTo(width,y*zoom);}}
            if(selection){guides.graphics.lineStyle(1,0xF4C45C,1);guides.graphics.drawRect(selection.x*zoom,selection.y*zoom,selection.width*zoom,selection.height*zoom);}
        }
        private function px():int{return Math.max(0,Math.min(imageWidth-1,int(mouseX/zoom)));}
        private function py():int{return Math.max(0,Math.min(imageHeight-1,int(mouseY/zoom)));}
        private function down(event:MouseEvent):void {
            if(!pixels||!layers.length)return;setFocus();sx=lx=px();sy=ly=py();
            if(tool=="picker"){var at:int=(sy*imageWidth+sx)*4;color=(uint(composed[at])<<24)|(uint(composed[at+1])<<16)|(uint(composed[at+2])<<8)|uint(composed[at+3]);dispatchEvent(new Event("picked"));return;}
            if(layers[layer].locked&&tool!="select")return;
            dragging=true;drawing=true;pointerStage=stage;pointerStage.addEventListener(MouseEvent.MOUSE_UP,up);
            if(tool=="fill"){fill(sx,sy);draw();}
            else if(tool=="pencil"||tool=="eraser"){dot(sx,sy);draw();}
        }
        private function pointerMove(event:MouseEvent):void {
            coordinate=px()+", "+py()+" px";
            if(!dragging)return;var x:int=px(),y:int=py();
            if(tool=="pencil"||tool=="eraser"){line(lx,ly,x,y);lx=x;ly=y;draw();}
            else if(tool=="select"){selection=new Rectangle(Math.min(sx,x),Math.min(sy,y),Math.abs(x-sx)+1,Math.abs(y-sy)+1);overlay();}
        }
        private function up(event:MouseEvent):void {
            if(pointerStage)pointerStage.removeEventListener(MouseEvent.MOUSE_UP,up);pointerStage=null;if(!dragging)return;dragging=false;
            var x:int=px(),y:int=py();
            if(tool=="line")line(sx,sy,x,y);
            else if(tool=="rect"){line(sx,sy,x,sy);line(x,sy,x,y);line(x,y,sx,y);line(sx,y,sx,sy);}
            else if(tool=="ellipse"){
                var cx:Number=(sx+x)/2,cy:Number=(sy+y)/2,rx:Number=Math.abs(x-sx)/2,ry:Number=Math.abs(y-sy)/2;
                if(rx==0||ry==0)line(sx,sy,x,y);else for(var i:int=0;i<720;i++){var a:Number=i*Math.PI/360;dot(Math.round(cx+rx*Math.cos(a)),Math.round(cy+ry*Math.sin(a)));}
            }else if(tool=="select")selection=new Rectangle(Math.min(sx,x),Math.min(sy,y),Math.abs(x-sx)+1,Math.abs(y-sy)+1);
            else if(tool=="move"&&selection)moveSelection(x-sx,y-sy);
            drawing=false;draw();if(tool!="select")dispatchEvent(new Event("stroke"));
        }
        private function dot(x:int,y:int):void {
            var size:int=(tool=="pencil"||tool=="eraser")?brushSize:1;
            for(var yy:int=0;yy<size;yy++)for(var xx:int=0;xx<size;xx++){
                var tx:int=x+xx,ty:int=y+yy;if(tx<0||ty<0||tx>=imageWidth||ty>=imageHeight)continue;if(selection&&!selection.contains(tx,ty)&&tool!="move")continue;
                var at:int=(ty*imageWidth+tx)*4,c:uint=tool=="eraser"?0:color;pixels[at]=c>>>24;pixels[at+1]=(c>>>16)&255;pixels[at+2]=(c>>>8)&255;pixels[at+3]=c&255;
            }
        }
        private function line(x0:int,y0:int,x1:int,y1:int):void {var dx:int=Math.abs(x1-x0),dy:int=-Math.abs(y1-y0),ix:int=x0<x1?1:-1,iy:int=y0<y1?1:-1,err:int=dx+dy;while(true){dot(x0,y0);if(x0==x1&&y0==y1)break;var e:int=2*err;if(e>=dy){err+=dy;x0+=ix;}if(e<=dx){err+=dx;y0+=iy;}}}
        private function same(at:int,rgba:Array):Boolean {return pixels[at]==rgba[0]&&pixels[at+1]==rgba[1]&&pixels[at+2]==rgba[2]&&pixels[at+3]==rgba[3];}
        private function fill(x:int,y:int):void {
            var at:int=(y*imageWidth+x)*4,rgba:Array=[pixels[at],pixels[at+1],pixels[at+2],pixels[at+3]],seen:ByteArray=new ByteArray(),queue:Array=[y*imageWidth+x];seen.length=imageWidth*imageHeight;
            for(var head:int=0;head<queue.length;head++){var index:int=queue[head];if(index<0||index>=seen.length||seen[index])continue;seen[index]=1;var px:int=index%imageWidth,py:int=int(index/imageWidth);if(!same(index*4,rgba)||(selection&&!selection.contains(px,py)))continue;dot(px,py);if(px>0)queue.push(index-1);if(px+1<imageWidth)queue.push(index+1);if(py>0)queue.push(index-imageWidth);if(py+1<imageHeight)queue.push(index+imageWidth);}seen.clear();
        }
        public function copySelection():void {
            var rect:Rectangle=selection||new Rectangle(0,0,imageWidth,imageHeight);if(copied)copied.clear();copied=new ByteArray();copyW=rect.width;copyH=rect.height;
            for(var y:int=0;y<copyH;y++)for(var x:int=0;x<copyW;x++){var at:int=((int(rect.y)+y)*imageWidth+int(rect.x)+x)*4;copied.writeBytes(pixels,at,4);}copied.position=0;
        }
        public function pasteSelection():void {if(!copied||layers[layer].locked)return;pasteAt(selection?selection.x:0,selection?selection.y:0);draw();dispatchEvent(new Event("stroke"));}
        private function pasteAt(ox:int,oy:int):void {for(var y:int=0;y<copyH;y++)for(var x:int=0;x<copyW;x++){var tx:int=x+ox,ty:int=y+oy;if(tx<0||ty<0||tx>=imageWidth||ty>=imageHeight)continue;var at:int=(ty*imageWidth+tx)*4,source:int=(y*copyW+x)*4;for(var c:int=0;c<4;c++)pixels[at+c]=copied[source+c];}}
        private function moveSelection(dx:int,dy:int):void {copySelection();for(var y:int=selection.y;y<selection.bottom;y++)for(var x:int=selection.x;x<selection.right;x++){var at:int=(y*imageWidth+x)*4;for(var c:int=0;c<4;c++)pixels[at+c]=0;}pasteAt(selection.x+dx,selection.y+dy);selection=selection.clone();selection.offset(dx,dy);selection=selection.intersection(new Rectangle(0,0,imageWidth,imageHeight));}
        private function key(event:KeyboardEvent):void {
            if(event.ctrlKey&&event.keyCode==Keyboard.C){copySelection();event.preventDefault();}
            else if(event.ctrlKey&&event.keyCode==Keyboard.V){pasteSelection();event.preventDefault();}
            else if(event.ctrlKey&&event.keyCode==Keyboard.Z){dispatchEvent(new Event(event.shiftKey?"redo":"undo"));event.preventDefault();}
            else if(event.keyCode==Keyboard.ESCAPE){selection=null;overlay();}
            else if(event.keyCode==Keyboard.B)tool="pencil";else if(event.keyCode==Keyboard.E)tool="eraser";else if(event.keyCode==Keyboard.I)tool="picker";else if(event.keyCode==Keyboard.G)tool="fill";
        }
        private function removed(event:Event):void {if(pointerStage)pointerStage.removeEventListener(MouseEvent.MOUSE_UP,up);pointerStage=null;dragging=drawing=false;}
        public function dispose():void {removed(null);if(surface)surface.dispose();surface=null;display.bitmapData=null;if(pixels)pixels.clear();if(composed)composed.clear();if(previous)previous.clear();if(copied)copied.clear();layers=[];}
    }
}
