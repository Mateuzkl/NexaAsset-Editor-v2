package ob.components
{
    import com.mignari.components.ProgressWidget;
    import mx.controls.ProgressBar;
    import mx.core.FlexGlobals;
    import mx.managers.PopUpManager;

    /** Gives progress labels room for descenders and long counters at high DPI. */
    public class ReadableProgressWidget extends ProgressWidget
    {
        public function fitLabelText():void
        {
            var available:Number = Math.max(1, FlexGlobals.topLevelApplication.width - 32);
            var desired:Number = Math.min(560, available);
            for (var i:int = 0; i < numElements; i++)
            {
                var bar:ProgressBar = getElementAt(i) as ProgressBar;
                if (!bar) continue;
                bar.setStyle("fontSize", 12);
                bar.setStyle("fontWeight", "normal");
                bar.toolTip = bar.label;
                var textWidth:Number = bar.measureText(bar.label).width;
                desired = Math.max(desired, Math.min(available, textWidth + 40));
                // When the parent is narrow, reduce the font only as much as needed.
                var fontSize:int = 12;
                while (textWidth + 24 > available && fontSize > 9)
                {
                    bar.setStyle("fontSize", --fontSize);
                    textWidth = bar.measureText(bar.label).width;
                }
                bar.height = Math.max(28, bar.measureText(bar.label).height + 12);
                bar.minHeight = bar.height;
            }
            width = Math.max(1, desired);
            PopUpManager.centerPopUp(this);
        }
    }
}
