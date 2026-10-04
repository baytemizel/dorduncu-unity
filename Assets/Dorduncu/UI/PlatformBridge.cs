// Native store / ads / social sign-in hooks. The game talks only to this interface; plug in Unity IAP, an ads SDK
// (AdMob rewarded with server-side verification: pass our user id as the SSV user_id) and Google / Apple sign-in by
// assigning GameApp.Bridge before the app starts (e.g. from a [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] method).
using System;
using System.Collections.Generic;

namespace Dorduncu.UI
{
    public interface IPlatformBridge
    {
        bool StoreAvailable { get; }
        /// <summary>Buy a product; `receipt` gets the body for POST /v1/purchases/verify (e.g. {store:"apple", signedTransaction}).</summary>
        void Purchase(string productId, Action<Dictionary<string, object>> receipt, Action<string> failed);

        bool AdsAvailable { get; }
        /// <summary>Show a rewarded ad. The reward itself is granted by the server through the ad network's SSV callback.</summary>
        void ShowRewardedAd(string userId, Action<bool> finished);

        bool SignInAvailable(string provider);
        /// <summary>Native sign-in; `token` gets the provider ID token (Google / Apple / Huawei) or access token (Facebook).</summary>
        void SignIn(string provider, Action<string> token, Action<string> failed);
    }

    /// <summary>Default: no native integrations (editor, desktop). The game stays fully playable offline and online as a guest.</summary>
    public sealed class NoNativeBridge : IPlatformBridge
    {
        public bool StoreAvailable => false;
        public void Purchase(string productId, Action<Dictionary<string, object>> receipt, Action<string> failed) => failed("Mağaza bu platformda bağlı değil.");
        public bool AdsAvailable => false;
        public void ShowRewardedAd(string userId, Action<bool> finished) => finished(false);
        public bool SignInAvailable(string provider) => false;
        public void SignIn(string provider, Action<string> token, Action<string> failed) => failed("Bu platformda giriş sağlayıcısı bağlı değil.");
    }
}
