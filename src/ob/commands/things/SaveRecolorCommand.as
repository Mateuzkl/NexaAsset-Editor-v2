package ob.commands.things
{
    import com.mignari.workers.WorkerCommand;

    public class SaveRecolorCommand extends WorkerCommand
    {
        public function SaveRecolorCommand(request:uint, id:uint, category:String, revision:Number,
                                           options:Object, masks:Object, createNew:Boolean)
        {
            super(request, id, category, revision, options, masks, createNew);
        }
    }
}
