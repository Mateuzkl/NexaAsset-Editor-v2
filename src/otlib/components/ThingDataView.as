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

package otlib.components
{
    import flash.display.BitmapData;
    import flash.events.Event;
    import flash.geom.Matrix;
    import flash.geom.Point;
    import flash.geom.Rectangle;
    import flash.utils.getTimer;

    import mx.core.UIComponent;

    import otlib.animation.Animator;
    import otlib.animation.FrameDuration;
    import otlib.animation.FrameGroup;
    import otlib.geom.Rect;
    import otlib.things.FrameGroupType;
    import otlib.things.ThingCategory;
    import otlib.things.ThingData;
    import otlib.things.ThingType;
    import otlib.utils.OutfitData;
    import otlib.utils.SpriteExtent;

    [Event(name="change", type="flash.events.Event")]
    [Event(name="complete", type="flash.events.Event")]

    public class ThingDataView extends UIComponent
    {
        // --------------------------------------------------------------------------
        // PROPERTIES
        // --------------------------------------------------------------------------

        private var _thingData:ThingData;
        private var _proposedThingData:ThingData;
        private var _thingDataChanged:Boolean;
        private var _animator:Animator;
        private var _spriteSheet:BitmapData;
        private var _textureIndex:Vector.<Rect>;
        private var _activeFrameGroup:FrameGroup;
        private var _bitmap:BitmapData;
        private var _fillRect:Rectangle;
        private var _point:Point;
        private var _rectangle:Rectangle;
        private var _frame:int;
        private var _maxFrame:int;
        private var _playing:Boolean;
        private var _lastTime:Number;
        private var _time:Number;
        private var _patternX:uint;
        private var _patternY:uint;
        private var _patternZ:uint;
        private var _layer:uint;
        private var _outfitData:OutfitData;
        private var _drawBlendLayer:Boolean;
        private var _backgroundColor:Number;
        private var _frameGroupType:uint;
        private var _minSize:uint = 0;
        private var _scale:Number = 1;

        // Static Matrix for draw() optimization - avoid allocation per frame
        private static var _drawMatrix:Matrix;

        // --------------------------------------
        // Getters / Setters
        // --------------------------------------

        [Bindable]
        public function get thingData():ThingData
        {
            return _proposedThingData ? _proposedThingData : _thingData;
        }
        public function set thingData(value:ThingData):void
        {
            if (_thingData != value)
            {
                _proposedThingData = value;
                _thingDataChanged = true;
                invalidateProperties();
            }
        }

        public function get patternX():uint
        {
            return _patternX;
        }
        public function set patternX(value:uint):void
        {
            _patternX = value;
        }

        public function get patternY():uint
        {
            return _patternY;
        }
        public function set patternY(value:uint):void
        {
            _patternY = value;
        }

        public function get patternZ():uint
        {
            return _patternZ;
        }
        public function set patternZ(value:uint):void
        {
            _patternZ = value;
        }

        public function get frame():int
        {
            return _frame;
        }
        public function set frame(value:int):void
        {
            if (_frame != value)
            {
                _frame = value % _maxFrame;
                _time = 0;
                draw();

                if (hasEventListener(Event.CHANGE))
                    dispatchEvent(new Event(Event.CHANGE));
            }
        }

        public function get outfitData():OutfitData
        {
            return _outfitData;
        }
        public function set outfitData(value:OutfitData):void
        {
            if (_outfitData != value)
            {
                _outfitData = value;
                // Rebuild sprite sheet with new colors using stored _thingData
                if (_thingData)
                {
                    rebuildSpriteSheet();
                }
            }
        }

        public function get drawBlendLayer():Boolean
        {
            return _drawBlendLayer;
        }
        public function set drawBlendLayer(value:Boolean):void
        {
            _drawBlendLayer = value;
        }

        public function get backgroundColor():Number
        {
            return _backgroundColor;
        }
        public function set backgroundColor(value:Number):void
        {
            if (isNaN(_backgroundColor) && isNaN(value))
                return;

            if (_backgroundColor != value)
            {
                _backgroundColor = value;
                draw();
            }
        }

        public function get frameGroupType():uint
        {
            return _frameGroupType;
        }
        public function set frameGroupType(value:uint):void
        {
            _frameGroupType = value;
        }

        public function get minSize():uint
        {
            return _minSize;
        }
        public function set minSize(value:uint):void
        {
            if (_minSize != value)
            {
                _minSize = value;
                updateScale();
                draw();
            }
        }

        // --------------------------------------------------------------------------
        // CONSTRUCTOR
        // --------------------------------------------------------------------------

        public function ThingDataView()
        {
            _point = new Point();
            _rectangle = new Rectangle();
            _frame = -1;
            _lastTime = 0;
            _time = 0;

            addEventListener(Event.ADDED_TO_STAGE, addedToStageHandler);
            addEventListener(Event.REMOVED_FROM_STAGE, removedFromStageHandler);
        }

        // --------------------------------------------------------------------------
        // METHODS
        // --------------------------------------------------------------------------

        // --------------------------------------
        // Public
        // --------------------------------------

        public function firstFrame():void
        {
            frame = 0;
        }

        public function prevFrame():void
        {
            frame = Math.max(0, _frame - 1);
        }

        public function nextFrame():void
        {
            frame = _frame + 1;
        }

        public function lastFrame():void
        {
            frame = Math.max(0, _maxFrame - 1);
        }

        public function play():void
        {
            var frameGroup:FrameGroup = thingData.thing.getFrameGroup(frameGroupType);
            if (thingData && frameGroup && frameGroup.isAnimation)
                _playing = true;
        }

        public function pause():void
        {
            _playing = false;
        }

        public function stop():void
        {
            _playing = false;
            frame = 0;
        }

        // --------------------------------------
        // Override Protected
        // --------------------------------------

        override protected function commitProperties():void
        {
            super.commitProperties();

            if (_thingDataChanged)
            {
                setThingData(_proposedThingData);
                _proposedThingData = null;
                _thingDataChanged = false;
            }
        }

        // --------------------------------------
        // Private
        // --------------------------------------

        private function setThingData(thingData:ThingData):void
        {
            _thingData = thingData;
            rebuildSpriteSheet();
        }

        private function rebuildSpriteSheet():void
        {
            var thingData:ThingData = _thingData;

            if (thingData)
            {
                // For outfits with 2+ layers, create temporary colorized clone for sprite sheet
                var dataForSheet:ThingData = thingData;
                var originalFrameGroup:FrameGroup = thingData.thing.getFrameGroup(frameGroupType);

                // Only clone and colorize if outfit has multiple layers (colorization needs blend layer)
                if (thingData.thing.category == ThingCategory.OUTFIT && originalFrameGroup && originalFrameGroup.layers >= 2)
                {
                    if (!_outfitData)
                        _outfitData = new OutfitData();

                    dataForSheet = thingData.clone().colorize(_outfitData);
                }

                _activeFrameGroup = dataForSheet.thing.getFrameGroup(frameGroupType);
                _textureIndex = new Vector.<Rect>();

                // Dispose old sprite sheet to free memory immediately
                if (_spriteSheet)
                    _spriteSheet.dispose();

                _spriteSheet = dataForSheet.getSpriteSheet(_activeFrameGroup, _textureIndex, 0);

                var w:int = _activeFrameGroup.width * SpriteExtent.DEFAULT_SIZE;
                var h:int = _activeFrameGroup.height * SpriteExtent.DEFAULT_SIZE;

                if (!_bitmap || _bitmap.width != w || _bitmap.height != h)
                {
                    if (_bitmap)
                        _bitmap.dispose();
                    _bitmap = new BitmapData(w, h, true, 0);
                    _fillRect = _bitmap.rect;
                }
                else
                {
                    _bitmap.fillRect(_fillRect, 0);
                }
                _maxFrame = _activeFrameGroup.frames;
                _frame = 0;
                _playing = _activeFrameGroup.isAnimation ? _playing : false;

                width = _bitmap.width;
                height = _bitmap.height;

                updateScale();

                // Preview timing must not modify the loaded/exported animation data.
                var durations:Vector.<FrameDuration> = null;
                if (_activeFrameGroup.frameDurations)
                {
                    durations = new Vector.<FrameDuration>();
                    for each (var sourceDuration:FrameDuration in _activeFrameGroup.frameDurations)
                        durations.push(sourceDuration ? new FrameDuration(sourceDuration.minimum, sourceDuration.maximum) : null);
                }
                if (durations && _activeFrameGroup.type == FrameGroupType.WALKING && _activeFrameGroup.frames > 2)
                {
                    var duration:uint = 1000 / _activeFrameGroup.frames;
                    for (var i:uint = 0; i < _activeFrameGroup.frames; i++)
                    {
                        if (durations[i])
                        {
                            durations[i].minimum = duration;
                            durations[i].maximum = duration;
                        }
                        else
                        {
                            durations[i] = new FrameDuration(duration, duration);
                        }
                    }
                }

                if (_activeFrameGroup.isAnimation)
                {
                    _animator = new Animator(_activeFrameGroup.animationMode, _activeFrameGroup.loopCount, _activeFrameGroup.startFrame, durations, _activeFrameGroup.frames);
                    _animator.skipFirstFrame = (thingData.category == ThingCategory.OUTFIT && !thingData.thing.animateAlways && _activeFrameGroup.type != FrameGroupType.WALKING);
                }
            }
            else
            {
                _textureIndex = null;
                if (_spriteSheet) _spriteSheet.dispose();
                if (_bitmap) _bitmap.dispose();
                _spriteSheet = null;
                _animator = null;
                _bitmap = null;
                _maxFrame = -1;
                _frame = -1;
                _playing = false;
            }

            draw();
        }

        private function updateScale():void
        {
            if (!_bitmap)
            {
                _scale = 1;
                return;
            }

            // Always use original bitmap dimensions for scale calculation
            var originalWidth:uint = _bitmap.width;
            var originalHeight:uint = _bitmap.height;
            var bitmapSize:uint = Math.max(originalWidth, originalHeight);

            if (_minSize > 0 && bitmapSize < _minSize)
            {
                _scale = _minSize / bitmapSize;
            }
            else
            {
                _scale = 1;
            }

            width = originalWidth * _scale;
            height = originalHeight * _scale;
        }

        private function draw():void
        {
            graphics.clear();

            if (_spriteSheet)
            {
                if (!isNaN(_backgroundColor))
                {
                    graphics.beginFill(_backgroundColor);
                    graphics.drawRect(0, 0, _fillRect.width, _fillRect.height);
                    graphics.endFill();
                }

                if (!thingData || !thingData.thing)
                    return;

                // Use _activeFrameGroup which matches the sprite sheet structure
                var frameGroup:FrameGroup = _activeFrameGroup;
                if (!frameGroup)
                {
                    frameGroup = thingData.thing.getFrameGroup(frameGroupType);
                    if (!frameGroup)
                        return;
                }

                var layers:uint = _drawBlendLayer ? frameGroup.layers : 1;
                var px:uint = _patternX % frameGroup.patternX;
                var pz:uint = _patternZ % frameGroup.patternZ;

                _bitmap.fillRect(_fillRect, 0);

                for (var l:uint = 0; l < layers; l++)
                {
                    var index:int = frameGroup.getTextureIndex(l, px, 0, pz, _frame);
                    if (!_textureIndex || _textureIndex.length == 0)
                        continue;

                    if (index >= _textureIndex.length)
                        index = 0;

                    var rect:Rect = _textureIndex[index];

                    _rectangle.setTo(rect.x, rect.y, rect.width, rect.height);
                    _bitmap.copyPixels(_spriteSheet, _rectangle, _point, null, null, true);
                }

                // Reuse static matrix for scaling
                if (!_drawMatrix)
                    _drawMatrix = new Matrix();

                _drawMatrix.identity();
                _drawMatrix.scale(_scale, _scale);
                graphics.beginBitmapFill(_bitmap, _drawMatrix, false, true);
                graphics.drawRect(0, 0, _fillRect.width * _scale, _fillRect.height * _scale);
            }

            graphics.endFill();
        }

        // --------------------------------------
        // Event Handlers
        // --------------------------------------

        protected function addedToStageHandler(event:Event):void
        {
            addEventListener(Event.ENTER_FRAME, enterFramehandler);
        }

        protected function removedFromStageHandler(event:Event):void
        {
            removeEventListener(Event.ENTER_FRAME, enterFramehandler);
        }

        protected function enterFramehandler(event:Event):void
        {
            if (!_playing || !thingData)
            {
                return;
            }

            var elapsed:Number = getTimer();
            if (_animator)
            {
                _animator.update(elapsed);
                if (_animator.isComplete)
                {
                    if (_thingData.thing.animateAlways)
                    {
                        _animator.reset();
                    }
                    else
                    {
                        pause();
                        dispatchEvent(new Event(Event.COMPLETE));
                    }
                }

                frame = _animator.frame;
            }
        }
    }
}
