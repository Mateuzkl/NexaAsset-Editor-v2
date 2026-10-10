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

package otlib.components.renders
{
    import flash.display.BitmapData;
    import flash.display.Graphics;

    import spark.components.Label;
    import spark.components.supportClasses.ItemRenderer;
    import spark.primitives.BitmapImage;
    import spark.primitives.Rect;
    import mx.graphics.SolidColor;
    import mx.graphics.SolidColorStroke;
    import flash.events.MouseEvent;

    public class BitmapListRenderer extends ItemRenderer
    {
        private var _imageDisplay:BitmapImage;
        private var _labelDisplay:Label;
        private var _hovered:Boolean = false;
        private var _fill:Rect;
        private var _border:Rect;

        // Colors (Matching SpriteListRenderer/ThingListRenderer)
        private static const COLOR_NORMAL:uint = 0x1B2937;
        // But for consistency we use SpriteListRenderer values if we go manual.
        // Wait, SpriteListRenderer uses 0x535353.
        private static const COLOR_HOVERED:uint = 0x213143;
        private static const COLOR_SELECTED:uint = 0x193D55;
        private static const COLOR_BORDER:uint = 0x2C3C4D;
        private static const COLOR_IMAGE_BG:uint = 0x101C29;

        public function BitmapListRenderer()
        {
            super();
            this.height = 41;
            this.autoDrawBackground = false;
        }

        override protected function createChildren():void
        {
            super.createChildren();

            // 1. Fill
            _fill = new Rect();
            _fill.left = 0;
            _fill.right = 0;
            _fill.top = 0;
            _fill.bottom = 0;
            _fill.fill = new SolidColor(COLOR_NORMAL);
            // Note: MXML was autoDrawBackground=true, which usually is transparent or white depending on list.
            // But if we want ThingList look:
            addElement(_fill);

            // 2. Border
            _border = new Rect();
            _border.left = 0;
            _border.right = 0;
            _border.top = 0;
            _border.bottom = 0;
            _border.stroke = new SolidColorStroke(COLOR_BORDER, 0.1);
            addElement(_border);

            // 3. Image Background
            var bgGroupSpec:Rect = new Rect();
            bgGroupSpec.width = 36;
            bgGroupSpec.height = 36;
            bgGroupSpec.verticalCenter = 0;
            bgGroupSpec.left = 3;
            bgGroupSpec.fill = new SolidColor(COLOR_IMAGE_BG);
            bgGroupSpec.stroke = new SolidColorStroke(COLOR_BORDER);
            addElement(bgGroupSpec);

            // 4. Image
            _imageDisplay = new BitmapImage();
            _imageDisplay.width = 32;
            _imageDisplay.height = 32;
            _imageDisplay.verticalCenter = 0;
            _imageDisplay.left = 5;
            addElement(_imageDisplay);

            // 5. Label
            _labelDisplay = new Label();
            _labelDisplay.left = 42;
            _labelDisplay.verticalCenter = 0;
            addElement(_labelDisplay);

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

        override public function set selected(value:Boolean):void
        {
            super.selected = value;
            invalidateDisplayList();
        }

        override protected function updateDisplayList(unscaledWidth:Number, unscaledHeight:Number):void
        {
            super.updateDisplayList(unscaledWidth, unscaledHeight);

            var color:uint = COLOR_NORMAL;
            if (selected)
                color = COLOR_SELECTED;
            else if (_hovered)
                color = COLOR_HOVERED;

            if (_fill && _fill.fill is SolidColor)
            {
                SolidColor(_fill.fill).color = color;
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
            var bitmap:BitmapData = value as BitmapData;
            if (bitmap)
            {
                _imageDisplay.source = bitmap;
                _labelDisplay.text = this.itemIndex.toString();
            }
            else
            {
                _imageDisplay.source = null;
                _labelDisplay.text = "";
            }
        }
    }
}
