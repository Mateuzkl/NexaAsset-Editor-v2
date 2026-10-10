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

package store.renders
{
    import flash.events.MouseEvent;

    import mx.events.FlexEvent;
    import mx.graphics.SolidColor;
    import mx.graphics.SolidColorStroke;

    import spark.components.Button;
    import spark.components.BusyIndicator;
    import spark.components.Label;
    import spark.components.supportClasses.ItemRenderer;
    import spark.primitives.BitmapImage;
    import spark.primitives.Rect;

    import otlib.assets.Assets;
    import store.StoreAsset;
    import store.StoreList;
    import store.events.AssetStoreEvent;

    [ResourceBundle("strings")]
    public class StoreListItemRenderer extends ItemRenderer
    {
        private var _imageDisplay:BitmapImage;
        private var _nameLabel:Label;
        private var _authorLabel:Label;
        private var _importButton:Button;
        private var _indicator:BusyIndicator;

        private var _bgRect:Rect;
        private var _imageBgRect:Rect;

        private var _hovered:Boolean = false;

        private static const COLOR_NORMAL:uint = 0x1B2937;
        private static const COLOR_HOVERED:uint = 0x213143;
        private static const COLOR_SELECTED:uint = 0x193D55;

        public function StoreListItemRenderer()
        {
            super();
            this.width = 200;
            this.height = 68;
            this.autoDrawBackground = false;
        }

        override protected function createChildren():void
        {
            super.createChildren();

            // 1. Background
            _bgRect = new Rect();
            _bgRect.left = 0;
            _bgRect.right = 0;
            _bgRect.top = 0;
            _bgRect.bottom = 0;
            _bgRect.fill = new SolidColor(COLOR_NORMAL);
            _bgRect.stroke = new SolidColorStroke(0x2C3C4D);
            addElement(_bgRect);

            // 2. Image Background Group (64x64)
            // Left padding 2? MXML: HGroup padding=2. So x=2. y=2 (centered vertically in HGroup height 100%?).
            // HGroup height is 100% (68). 64 is fits.

            _imageBgRect = new Rect();
            _imageBgRect.fill = new SolidColor(0x101C29);
            _imageBgRect.stroke = new SolidColorStroke(0x2C3C4D);
            addElement(_imageBgRect);

            _indicator = new BusyIndicator();
            _indicator.setStyle("symbolColor", 0x2BB7F5);
            _indicator.setStyle("rotationInterval", 50);
            addElement(_indicator);

            _imageDisplay = new BitmapImage();
            addElement(_imageDisplay);

            // 3. Labels (VGroup)
            _nameLabel = new Label();
            _nameLabel.setStyle("fontSize", 14);
            _nameLabel.setStyle("fontWeight", "bold");
            addElement(_nameLabel);

            _authorLabel = new Label();
            _authorLabel.setStyle("fontSize", 14);
            _authorLabel.setStyle("color", 0x9AC9F8);
            addElement(_authorLabel);

            // 4. Button
            _importButton = new Button();
            _importButton.width = 22;
            _importButton.height = 22;
            _importButton.bottom = 2;
            _importButton.right = 2;
            // _importButton.toolTip set in data or updates? MXML: toolTip="@Resource(key='import', bundle='strings')"
            _importButton.setStyle("icon", Assets.IMPORT);
            _importButton.addEventListener(MouseEvent.CLICK, importClickHandler);
            addElement(_importButton);

            // Set tooltip for button
            _importButton.toolTip = resourceManager.getString("strings", "import");

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

            // Update Background Color
            var color:uint = COLOR_NORMAL;
            if (selected)
                color = COLOR_SELECTED;
            else if (_hovered)
                color = COLOR_HOVERED;

            if (_bgRect && _bgRect.fill is SolidColor)
            {
                SolidColor(_bgRect.fill).color = color;
            }

            // Layout
            var padding:Number = 2;

            // Image Group area: 64x64
            var imgGroupX:Number = padding;
            var imgGroupY:Number = (unscaledHeight - 64) / 2; // Center vertically ?
            if (imgGroupY < padding)
                imgGroupY = padding;

            _imageBgRect.x = imgGroupX;
            _imageBgRect.y = imgGroupY;
            _imageBgRect.width = 64;
            _imageBgRect.height = 64;

            // Center Indicator and Image in the 64x64 box
            if (_indicator.visible)
            {
                _indicator.x = imgGroupX + (64 - _indicator.width) / 2;
                _indicator.y = imgGroupY + (64 - _indicator.height) / 2;
            }

            if (_imageDisplay.source)
            {
                // If it has size? BitmapImage auto sizes.
                var iw:Number = _imageDisplay.getPreferredBoundsWidth();
                var ih:Number = _imageDisplay.getPreferredBoundsHeight();
                // Center it
                _imageDisplay.x = imgGroupX + (64 - iw) / 2;
                _imageDisplay.y = imgGroupY + (64 - ih) / 2;
            }

            // Labels VGroup area
            // x = imgGroupX + 64 + padding? MXML HGroup gap=8? gap default 6.
            var labelX:Number = imgGroupX + 64 + 8; // Assumed gap
            var labelW:Number = unscaledWidth - labelX - padding;

            // VGroup padding=4
            var labelY:Number = padding + 4;

            _nameLabel.x = labelX + 4; // padding
            _nameLabel.y = labelY;
            _nameLabel.width = labelW - 8;
            _nameLabel.setActualSize(labelW - 8, _nameLabel.getPreferredBoundsHeight());

            _authorLabel.x = labelX + 4;
            _authorLabel.y = labelY + _nameLabel.getPreferredBoundsHeight() + 2; // gap
            _authorLabel.width = labelW - 8;
            _authorLabel.setActualSize(labelW - 8, _authorLabel.getPreferredBoundsHeight());

            // Button
            _importButton.x = unscaledWidth - _importButton.width - 2;
            _importButton.y = unscaledHeight - _importButton.height - 2;
        }

        override public function set data(value:Object):void
        {
            super.data = value;
            var asset:StoreAsset = value as StoreAsset;

            if (asset)
            {
                _imageDisplay.source = asset.bitmap;
                _nameLabel.text = asset.name;
                _authorLabel.text = asset.author;
                _importButton.enabled = asset.loaded;
                _indicator.visible = !asset.loaded;
                this.toolTip = asset.error;
            }
            else
            {
                _imageDisplay.source = null;
                _nameLabel.text = "";
                _authorLabel.text = "";
                _importButton.enabled = false;
                _indicator.visible = false;
                this.toolTip = null;
            }

            invalidateDisplayList();
        }

        private function importClickHandler(event:MouseEvent):void
        {
            var list:StoreList = owner as StoreList;
            var asset:StoreAsset = data as StoreAsset;

            if (list && asset && asset.loaded)
                list.dispatchEvent(new AssetStoreEvent(AssetStoreEvent.IMPORT_ASSET, asset.data));
        }
    }
}
