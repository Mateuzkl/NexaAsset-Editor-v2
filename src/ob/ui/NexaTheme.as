package ob.ui
{
    import mx.core.mx_internal;
    import mx.styles.CSSStyleDeclaration;
    import mx.styles.IStyleManager2;
    import mx.utils.ObjectProxy;

    /** Palettes for editor controls and chrome. */
    public final class NexaTheme
    {
        public static const BLUE:String = "blue";
        public static const BLACK:String = "black";
        public static const PURPLE:String = "purple";

        private static const BLUE_COLORS:ObjectProxy = new ObjectProxy({
            canvas:0x0B141E,
            status:0x0A1520,
            navigation:0x0D1926,
            toolbar:0x111D2A,
            panel:0x16232F,
            surface:0x1B2937,
            hover:0x213143,
            input:0x101C29,
            preview:0x101B26,
            studio:0x101C28,
            border:0x2C3C4D,
            strongBorder:0x3B5368,
            text:0xF0F5FB,
            secondary:0xADC0D4,
            muted:0x8498AC,
            symbol:0xB5C8DD,
            accent:0x2BB7F5,
            selected:0x193D55,
            navSelected:0x102A3D,
            checkerDark:0x182633,
            checkerLight:0x202E3C
        });
        private static const BLACK_COLORS:ObjectProxy = new ObjectProxy({
            canvas:0x101010,
            status:0x121212,
            navigation:0x151515,
            toolbar:0x1A1A1A,
            panel:0x202020,
            surface:0x272727,
            hover:0x303030,
            input:0x171717,
            preview:0x151515,
            studio:0x161616,
            border:0x3C3C3C,
            strongBorder:0x585858,
            text:0xF2F2F2,
            secondary:0xC0C0C0,
            muted:0x999999,
            symbol:0xCACACA,
            accent:0xB7D8EE,
            selected:0x344451,
            navSelected:0x29343D,
            checkerDark:0x222222,
            checkerLight:0x2D2D2D
        });
        private static const PURPLE_COLORS:ObjectProxy = new ObjectProxy({
            canvas:0x14101C,
            status:0x191223,
            navigation:0x1D1528,
            toolbar:0x251A33,
            panel:0x2C203C,
            surface:0x362749,
            hover:0x423059,
            input:0x21172E,
            preview:0x1D1528,
            studio:0x20162B,
            border:0x534064,
            strongBorder:0x73588B,
            text:0xF6EFFB,
            secondary:0xD4C3E4,
            muted:0xAA94BD,
            symbol:0xE0CCEF,
            accent:0xCB9EFF,
            selected:0x4C3065,
            navSelected:0x3B2552,
            checkerDark:0x2A2035,
            checkerLight:0x342843
        });

        [Bindable]
        public static var palette:ObjectProxy = BLUE_COLORS;

        public static function normalize(id:String):String
        {
            return id == BLACK || id == PURPLE ? id : BLUE;
        }

        public static function apply(id:String, styles:IStyleManager2):void
        {
            var colors:ObjectProxy = id == BLACK ? BLACK_COLORS : id == PURPLE ? PURPLE_COLORS : BLUE_COLORS;
            palette = colors;
            applyStyles(styles, "global", {
                chromeColor:colors.surface, rollOverColor:colors.hover, selectionColor:colors.selected,
                color:colors.secondary, contentBackgroundColor:colors.panel, focusedTextSelectionColor:colors.selected,
                symbolColor:colors.symbol, focusColor:colors.accent, modalTransparencyColor:colors.canvas
            });
            var canvas:Object = {backgroundColor:colors.canvas};
            applyStyles(styles, "spark.components.Application", canvas);
            applyStyles(styles, "spark.components.WindowedApplication", canvas);
            applyStyles(styles, "spark.components.Window", canvas);
            applyStyles(styles, ".nexaNavigationSurface", {backgroundColor:colors.navigation, borderColor:colors.border});
            applyStyles(styles, ".nexaToolbarSurface", {backgroundColor:colors.toolbar, borderColor:colors.border});

            var panel:Object = {backgroundColor:colors.panel, borderColor:colors.border};
            applyStyles(styles, "spark.components.BorderContainer", panel);
            applyStyles(styles, "com.mignari.components.GroupBox", panel);
            applyStyles(styles, "com.mignari.components.GroupCheckBox", panel);
            var dialog:Object = {backgroundColor:colors.toolbar, color:colors.secondary};
            applyStyles(styles, "com.mignari.components.TitleWindow", dialog);
            applyStyles(styles, "nail.components.controls.Alert", dialog);
            applyStyles(styles, "spark.components.List", {contentBackgroundColor:colors.panel, borderColor:colors.border});
            applyStyles(styles, "spark.components.TextArea", {color:colors.text, contentBackgroundColor:colors.input});
            applyStyles(styles, "spark.components.TextInput", {
                color:colors.text, contentBackgroundColor:colors.input, borderColor:colors.border,
                focusedTextSelectionColor:colors.selected
            });
            applyStyles(styles, "mx.controls.ToolTip", {
                color:colors.text, backgroundColor:colors.panel, borderColor:colors.strongBorder
            });
            var divider:Object = {dividerColor:colors.border};
            applyStyles(styles, "mx.containers.HDividedBox", divider);
            applyStyles(styles, "mx.containers.VDividedBox", divider);
            applyStyles(styles, "otlib.components.CheckerBoard", {cellColors:[colors.checkerDark, colors.checkerLight]});
            // Rebuild the style caches once for the entire palette.
            styles.styleDeclarationsChanged();
        }

        private static function applyStyles(styles:IStyleManager2, selector:String, colors:Object):void
        {
            var declaration:CSSStyleDeclaration = styles.getStyleDeclaration(selector);
            if (!declaration)
                return;

            for (var property:String in colors)
                declaration.mx_internal::setLocalStyle(property, colors[property]);
        }
    }
}
