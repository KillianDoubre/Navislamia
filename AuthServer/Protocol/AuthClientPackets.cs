namespace Navislamia.AuthServer.Protocol;

public enum AuthClientPackets : ushort
{
    TS_AC_RESULT = 10000,
    TS_CA_VERSION = 10001,
    TS_CA_RSA_PUBLIC_KEY = 71,
    TS_AC_AES_KEY_IV = 72,
    TS_CA_ACCOUNT = 10010,
    TS_CA_SERVER_LIST = 10021,
    TS_AC_SERVER_LIST = 10022,
    TS_CA_SELECT_SERVER = 10023,
    TS_AC_SELECT_SERVER = 10024,
}
