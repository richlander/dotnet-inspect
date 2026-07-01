public class Program {
    public static void Main() {}
    public static uint TestMethod(bool c) {
        uint y = 2;
        return c ? unchecked((uint)-1) : y;
    }
}
