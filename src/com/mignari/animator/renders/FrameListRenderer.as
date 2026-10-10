/*
*  Copyright (c) 2014-2023 Object Builder <https://github.com/ottools/ObjectBuilder>
*
*  Permission is hereby granted, free of charge, to any person obtaining a copy
*  of this software and associated documentation files (the "Software"), to deal
*  in the Software without restriction, including without limitation the rights
*  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
*  copies of the Software, and to permit persons to whom the Software is
*  furnished to do so, subject to the following conditions:
*
*  The above copyright notice and this permission notice shall be included in
*  all copies or substantial portions of the Software.
*
*  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
*  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
*  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
*  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
*  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
*  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
*  THE SOFTWARE.
*/

package com.mignari.animator.renders
{
    import flash.display.NativeMenuItem;
    import flash.events.ContextMenuEvent;
    import flash.events.Event;
    import flash.events.MouseEvent;
    import flash.ui.ContextMenu;
    import flash.ui.ContextMenuItem;

    import mx.events.FlexEvent;
    import mx.resources.IResourceManager;
    import mx.resources.ResourceManager;
    import mx.graphics.SolidColor;
    import mx.graphics.SolidColorStroke;

    import spark.components.Label;
    import spark.components.supportClasses.ItemRenderer;
    import spark.primitives.BitmapImage;
    import spark.primitives.Rect;
    import spark.primitives.Line;

    import com.mignari.animator.Frame;
    import com.mignari.animator.components.FrameList;
    import com.mignari.animator.events.FrameListEvent;

    import otlib.components.ListBase;
    import otlib.core.otlib_internal;

    use namespace otlib_internal;

    [ResourceBundle("strings")]
    public class FrameListRenderer extends ItemRenderer
    {
        private var _imageDisplay:BitmapImage;
        private var _labelDisplay:Label;

        private var _fill:Rect;
        private var _bgRect:Rect;
        private var _lineTop:Line;
        private var _lineLeft:Line;

        private var _hovered:Boolean = false;

        private static const COLOR_NORMAL:uint = 0x1B2937;
        private static const COLOR_HOVERED:uint = 0x213143;
        private static const COLOR_SELECTED:uint = 0x193D55;

        public function FrameListRenderer()
        {
            super();
            this.minHeight = 41;
            this.autoDrawBackground = false;
        }

        override protected function createChildren():void
        {
            super.createChildren();

            // 1. Fill/Border Container
            _fill = new Rect();
            _fill.left = 0;
            _fill.right = 0;
            _fill.top = 0;
            _fill.bottom = 0;
            _fill.fill = new SolidColor(COLOR_NORMAL);
            _fill.stroke = new SolidColorStroke(0x2C3C4D, 0.1);
            addElement(_fill);

            // 2. Image Background Group elements
            // To simulate the Group logic, we just place these rects/lines where they should be relative to image
            // But wait, the image is in a VGroup with padding.

            _bgRect = new Rect();
            _bgRect.fill = new SolidColor(0x101C29);
            _bgRect.stroke = new SolidColorStroke(0x2C3C4D);
            addElement(_bgRect);

            _lineTop = new Line();
            _lineTop.stroke = new SolidColorStroke(0x2C3C4D);
            addElement(_lineTop);

            _lineLeft = new Line();
            _lineLeft.stroke = new SolidColorStroke(0x2C3C4D);
            addElement(_lineLeft);

            _imageDisplay = new BitmapImage();
            _imageDisplay.width = 50;
            _imageDisplay.height = 50;
            _imageDisplay.smooth = true;
            addElement(_imageDisplay);

            _labelDisplay = new Label();
            addElement(_labelDisplay);

            this.addEventListener(FlexEvent.CREATION_COMPLETE, creationCompleteHandler);
            this.addEventListener(MouseEvent.ROLL_OVER, rollOverHandler);
            this.addEventListener(MouseEvent.ROLL_OUT, rollOutHandler);
        }

        protected function rollOverHandler(event:MouseEvent):void
        {
            _hovered = true;
            invalidateDisplayList();
        }

        protected function rollOutHandler(event:MouseEvent):void
        {
            _hovered = false;
            invalidateDisplayList();
        }

        protected function creationCompleteHandler(event:FlexEvent):void
        {
            if (owner is ListBase && ListBase(owner).contextMenuEnabled)
            {
                var cm:ContextMenu = createContextMenu();
                cm.addEventListener(Event.SELECT, contextMenuSelectHandler);
                cm.addEventListener(Event.DISPLAYING, contextMenuDisplayingHandler);
                this.contextMenu = cm;
            }
        }

        override protected function updateDisplayList(unscaledWidth:Number, unscaledHeight:Number):void
        {
            super.updateDisplayList(unscaledWidth, unscaledHeight);

            // Background color
            var color:uint = COLOR_NORMAL;
            if (selected)
                color = COLOR_SELECTED;
            else if (_hovered)
                color = COLOR_HOVERED;

            if (_fill && _fill.fill is SolidColor)
            {
                SolidColor(_fill.fill).color = color;
            }

            // Layout (Simulating VGroup)
            var paddingLeft:int = 7;
            var paddingRight:int = 5;
            var paddingTop:int = 7;
            var paddingBottom:int = 5;

            var availableWidth:Number = unscaledWidth - paddingLeft - paddingRight;

            // Image Group Layout
            // The inner group was min 32x32. The image is 50x50.
            // Let's assume the "Group" area wraps the image.
            var imgGroupWidth:Number = 50;
            var imgGroupHeight:Number = 50;
            // Center horizontally
            var startX:Number = paddingLeft + (availableWidth - imgGroupWidth) / 2;
            var startY:Number = paddingTop;

            // Rect: left=-1, right=-1, top=-1, bottom=-1 relative to Group (which contains Image)
            // So if Image is at startX, startY to startX+50, startY+50
            // Rect is startX-1 to startX+50+1

            if (_bgRect)
            {
                _bgRect.x = startX - 1;
                _bgRect.y = startY - 1;
                _bgRect.width = imgGroupWidth + 2;
                _bgRect.height = imgGroupHeight + 2;
            }

            if (_lineTop)
            {
                _lineTop.xFrom = startX - 1;
                _lineTop.xTo = startX + imgGroupWidth + 1; // right=-1
                _lineTop.y = startY - 1;
            }

            if (_lineLeft)
            {
                _lineLeft.x = startX - 1;
                _lineLeft.yFrom = startY - 1;
                _lineLeft.yTo = startY + imgGroupHeight + 1;
            }

            if (_imageDisplay)
            {
                _imageDisplay.x = startX;
                _imageDisplay.y = startY;
            }

            // Label below Image
            if (_labelDisplay)
            {
                // Determine label position
                var labelHeight:Number = _labelDisplay.getPreferredBoundsHeight();
                var labelWidth:Number = _labelDisplay.getPreferredBoundsWidth();
                var labelY:Number = startY + imgGroupHeight + 5; // +5 gap? VGroup gap not specified, default is 6.
                // Actually VGroup gap default is 6.

                _labelDisplay.x = paddingLeft + (availableWidth - labelWidth) / 2;
                _labelDisplay.y = labelY;
                _labelDisplay.setActualSize(labelWidth, labelHeight);
            }
        }

        override public function set itemIndex(value:int):void
        {
            super.itemIndex = value;
            if (_labelDisplay)
                _labelDisplay.text = value.toString();
        }

        override public function set data(value:Object):void
        {
            super.data = value;
            var frame:Frame = value as Frame;
            if (frame)
            {
                _imageDisplay.source = frame.getBitmap();
                _labelDisplay.text = this.itemIndex.toString();
            }
            else
            {
                _imageDisplay.source = null;
                _labelDisplay.text = "";
            }
        }

        // Context Menu

        protected function contextMenuSelectHandler(event:Event):void
        {
            if (owner is FrameList)
            {
                var type:String = NativeMenuItem(event.target).data as String;
                FrameList(owner).onContextMenuSelect(this.itemIndex, type);
            }
        }

        protected function contextMenuDisplayingHandler(event:Event):void
        {
            if (owner is FrameList)
            {
                FrameList(owner).onContextMenuDisplaying(this.itemIndex, ContextMenu(event.target));
            }
        }

        private static function createContextMenu():ContextMenu
        {
            var resource:IResourceManager = ResourceManager.getInstance();
            var duplicateMenu:ContextMenuItem = new ContextMenuItem(resource.getString("strings", "duplicateFrame"));
            duplicateMenu.data = FrameListEvent.DUPLICATE;
            var removeMenu:ContextMenuItem = new ContextMenuItem(resource.getString("strings", "deleteFrame"));
            removeMenu.data = FrameListEvent.REMOVE;
            var menu:ContextMenu = new ContextMenu();
            menu.customItems = [duplicateMenu, removeMenu];
            return menu;
        }
    }
}
