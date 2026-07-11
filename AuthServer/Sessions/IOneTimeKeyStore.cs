namespace Navislamia.AuthServer.Sessions;

public interface IOneTimeKeyStore
{
    long Issue(string username, int accountId, int serverIdx);

    int? Verify(string username, long key);
}
