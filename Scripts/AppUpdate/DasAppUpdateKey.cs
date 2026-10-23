// The public half of the DAS app signing key. Every update is checked against it before anything is installed, so only
// releases signed with the DAS admin's private key can ever be installed - even if the server or the bucket were
// changed by someone else.
//
// Written by Unity: DAS > App Updates > Create Signing Key. Empty = app updates are switched off in this build.
// The same key goes in VRSP's .env as DAS_APP_PUBLIC_KEY.
namespace VehicleMeasurement.AppUpdate
{
    public static class DasAppUpdateKey
    {
        public const string PublicKeyXml = "";
    }
}
