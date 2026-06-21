using Unity.Netcode;
using Unity.Netcode.Components;

namespace KartGame.Multiplayer
{
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
