using System;
using UnityEngine;

namespace GameJolt.API
{
	/// <summary>
	///     API Settings Config Object.
	/// </summary>
	[Serializable]
    public class Settings : ScriptableObject
    {
        #region Serialized Fields

        [Header("Game")]
        [SerializeField]
        [Tooltip(
            "The game ID. It can be found on the Game Jolt website under Dashboard > YOUR-GAME > Game API > API Settings.")]
        private int gameId;

        [SerializeField]
        [Tooltip(
            "The game Private Key. It can be found on the Game Jolt website under Dashboard > YOUR-GAME > Game API > API Settings.")]
        private string privateKey;

        [Header("Settings")]
        [SerializeField]
        [Tooltip("The time in seconds before an API call should timeout and return failure.")]
        private float timeout = 10f;

        [SerializeField] [Tooltip("Automatically create and ping sessions once a user has been authentified.")]
        private bool autoPing = true;

        [SerializeField] [Tooltip("Automatically show a message if a user has successfully signed in our out")]
        private bool autoSignInOutMessage;

        [SerializeField]
        [Tooltip("If AutoSignInOutMessage is set to true, this message will be shown if a user has signed in.")]
        private string signInMessage = "Signed in as '{0}'";

        [SerializeField]
        [Tooltip("If AutoSignInOutMessage is set to true, this message will be shown if a user has signed out.")]
        private string signOutMessage = "Signed out";

        [SerializeField] [Tooltip("Cache High Score Tables and Trophies information for faster display.")]
        private bool useCaching = true;

        [SerializeField] [Tooltip("The key used to encrypt the user credentials.")]
        private string encryptionKey = "";

        [SerializeField]
        [Tooltip("Set LogLevel for all GameJolt API log messages. Messages below this level will be discarded.")]
        private LogHelper.LogLevel logLevel = LogHelper.LogLevel.Warning;

        [SerializeField] [Tooltip("List of trophies which are only shown if the user has achieved them.")]
        private int[] secretTrophies;

        [Header("Debug")] [SerializeField] [Tooltip("AutoConnect in the Editor as if the game was hosted on GameJolt.")]
        private bool autoConnect;

        [SerializeField] [Tooltip("The username to use for AutoConnect.")]
        private string user;

        [SerializeField] [Tooltip("The token to use for AutoConnect.")]
        private string token;

        #endregion

        #region Public getter

        public int GameId => gameId;
        internal string PrivateKey => privateKey;

        public float Timeout => timeout;
        public bool AutoPing => autoPing;

        public bool AutoSignInOutMessage => autoSignInOutMessage;
        public string SignInMessage => signInMessage;
        public string SignOutMessage => signOutMessage;

        public bool UseCaching => useCaching;

        public string EncryptionKey
        {
            get { return encryptionKey; }
#if UNITY_EDITOR
            set { encryptionKey = value; }
#endif
        }

        public LogHelper.LogLevel LogLevel => logLevel;
        public int[] SecretTrophies => secretTrophies;

#if UNITY_EDITOR
        public bool DebugAutoConnect => autoConnect;
        public string DebugUser => user;
        public string DebugToken => token;
#else
		public bool DebugAutoConnect { get { return false; } }
		public string DebugUser { get { return string.Empty; } }
		public string DebugToken { get { return string.Empty; } }
#endif

        #endregion
    }
}