using SS14.Android;

var contacts = new AndroidTouchContacts();
var count = 0;
void Check(long[] expected, string name)
{
    if (!contacts.Snapshot().SequenceEqual(expected)) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    count++;
}
contacts.Update([0]);
Check([1], "Android pointer zero maps to SDL finger one");
contacts.Update([0, 4]);
Check([1, 5], "second finger retains original pointer identity");
contacts.Update([0, 4], 0);
Check([5], "lifting stick finger leaves the other finger active");
contacts.Update([4, 8]);
contacts.Update([4, 8], 1);
Check([5], "lifting second finger keeps first finger active");
contacts.Clear();
Check([], "last finger lift or Android cancellation clears contacts");
contacts.Update([0]);
var external = contacts.Snapshot();
external[0] = 999;
Check([1], "engine cannot mutate Android pointer ownership snapshot");
contacts.Clear();
contacts.Update([0]);
Check([1], "reused Android pointer starts a new contact after cancellation");
Console.WriteLine($"PASS {count} Android pointer checks");
