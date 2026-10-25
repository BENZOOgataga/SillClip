using System.Collections.ObjectModel;

namespace WindowSill.ClipboardHistory.Core;

internal static class CollectionReconciler
{
    internal static void Update<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (int index = 0; index < desired.Count; index++)
        {
            if (index < target.Count && ReferenceEquals(target[index], desired[index])) continue;
            int existing = -1;
            for (int candidate = index + 1; candidate < target.Count; candidate++)
                if (ReferenceEquals(target[candidate], desired[index])) { existing = candidate; break; }
            if (existing >= 0) target.Move(existing, index);
            else target.Insert(index, desired[index]);
        }
        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }
}
