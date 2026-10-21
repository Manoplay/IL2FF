using Enums;
using Fisher;
using LittleBigPlanet;

namespace Samples;

[Entrypoint]
[Fisher.Guid(Value = 1573052)]
public class TweakableSample : Tweakable
{

    [Divergent]
    private string _text = "Insert text";
    [Divergent]
    private Resource _image = null!;

    public override bool StartTweakingObject(int value)
    {
        _image = Resource.LoadTextureByFilename(10732u, true);
        return true;
    }

    public override void UpdateUI(Gooey gooey, Poppet poppet)
    {
        if (!StartTweakFrame(gooey, 1698230541)) return;
        Poppet.DoSectionBreak(gooey, LAMS.Translate(1719883962), true);
        if (gooey.StartFrame())
        {
            gooey.SetFrameSizing(-1.0f, 0.0f);
            gooey.SetFrameLayout(3, 0);
            gooey.SetFrameDefaultChildSpacing(10, 10);
            gooey.DoText("Message", (int)EGooeyTextStyle.GTS_T5);
            gooey.DoHorizontalBreak(0);
            int selected = gooey.DoTextEntry("Name", _text, 1, 0x01800000, false, 10);
            if ((selected & 0x00800000) != 0)
            {
                _text = gooey.GetTextEntryString();
            }
            gooey.DoHorizontalBreak(0);
            int choice = gooey.DoButton("Present", 1);
            if ((choice & 256) != 0)
            {
                GameMessagingManager.Messaging_NewMessage(26, _text, "OK", null, null);
            }
            gooey.EndFrame();
        }

        Poppet.DoSectionBreak(gooey, "Image", true);
        if (gooey.StartFrame())
        {
            gooey.SetFrameSizing(-1.0f, 0.0f);
            gooey.DoImage(_image, new Vec2(256, 256), new Vec4(1, 1, 1, 1));
            gooey.EndFrame();
        }

        EndTweakFrame(gooey, poppet);
    }

    public override void StopTweakingObject()
    {
        
    }
}
