using MoonThemedGame.Interactions;

namespace MoonThemedGame.Repair
{
    // Repair actions share the same terminal hint treatment as world interactions.
    public static class RepairHud
    {
        public static void Hint(float centerX,float y,string text,int size=14)=>
            InteractionHud.Prompt(centerX,y,text,size);
        public static void Corner(string text)=>InteractionHud.Corner(text);
    }
}
