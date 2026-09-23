namespace Navislamia.Game.Network.Security;

public class Xrc4Cipher : ICipher
{
    private readonly Rc4Cipher _cipher;

    public Xrc4Cipher()
    {
        _cipher = new Rc4Cipher();
        Clear();
    }

    public void SetKey(string key) => _cipher.Init(key, key.Length);

    public void Clear() => _cipher.Init("Neat & Simple");

    /// <summary>Codes <paramref name="buffer"/> in place, advancing the keystream.</summary>
    public void Code(System.Span<byte> buffer) => _cipher.Code(buffer);

    public void Decode(byte[] source, byte[] destination, int length, bool isPeek = false)
    {
        if (isPeek)
        {
            TryCipher(source, destination, length);
        }
        else
        {
            DoCipher(source, destination, length);
        }
    }

    public void Encode(byte[] source, byte[] destination, int length, bool isPeek = false)
    {
        if (isPeek)
        {
            TryCipher(source, destination, length);
        }
        else
        {
            DoCipher(source, destination, length);
        }
    }

    private void TryCipher(byte[] source, byte[] destination, int length)
    {
        var backup = new State(_cipher.GetState());

        DoCipher(source, destination, length);

        _cipher.LoadStateFrom(backup);
    }

    private void DoCipher(byte[] source, byte[] destination, int length) => _cipher.Code(source, destination, length);

}