using System;

public class Fixture
{
    int _counter;
    int Next => _counter++;

    public int SwitchStorePropertySelector(int n)
    {
        int x = -1;
        switch (Next)
        {
            case 0:
                x = x + Next;
                break;
            case 1:
                x = x + Next;
                break;
            default:
                x = x + Next;
                break;
        }
        return x + Next;
    }
}
