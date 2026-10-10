package otlib.utils
{
    import otlib.core.ClientFeatures;
    import otlib.resources.Resources;

    /** Compare a manual input choice with bounded binary evidence and descriptor hints. */
    public final class AssetFeatureValidator
    {
        public static function compare(info:ClientInfo, selected:ClientFeatures):Object
        {
            var messages:Array = [], blocking:Boolean = false;
            if (!info || !info.features || !selected) return {text:"", blocking:false};
            var source:String = Resources.getString(info.featureDescriptor ? "assetHintOTFI" : "assetHintVersion");
            var expected:ClientFeatures = info.features;
            if (selected.extended != expected.extended)
            {
                messages.push(mismatch("extended", expected.extended, info.extendedKnown ? Resources.getString("assetHintHeader") : source, info.extendedKnown));
                blocking = info.extendedKnown;
            }
            if (info.transparencyConflict)
            {
                messages.push(Resources.getString("assetMixedTransparency"));
                blocking = true;
            }
            else if (selected.transparency != expected.transparency)
            {
                messages.push(mismatch("transparency", expected.transparency, info.transparencyKnown ? Resources.getString("assetHintSamples") : source, info.transparencyKnown));
                blocking = blocking || info.transparencyKnown;
            }
            else if (!info.transparencyKnown)
                messages.push(Resources.getString("assetTransparencyUnverified"));
            if (info.hasMetadata)
            {
                // DAT does not expose these flags in a self-describing header.
                // Descriptor/version differences are hints, never proof of corruption.
                if (selected.improvedAnimations != expected.improvedAnimations)
                    messages.push(mismatch("improvedAnimations", expected.improvedAnimations, source));
                if (selected.frameGroups != expected.frameGroups)
                    messages.push(mismatch("frameGroups", expected.frameGroups, source));
            }
            return {text:messages.join("\n"), blocking:blocking};
        }

        private static function mismatch(key:String, enabled:Boolean, source:String, known:Boolean = false):String
        {
            return Resources.getString(known ? "assetFeatureMismatch" : "assetFeatureHint", Resources.getString(key),
                Resources.getString(enabled ? "assetOptionOn" : "assetOptionOff"), source);
        }
    }
}
