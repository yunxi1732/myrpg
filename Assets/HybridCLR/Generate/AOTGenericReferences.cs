using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

    // {{ AOT assemblies
    public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
    {
        "System.Core.dll",
        "System.dll",
        "Unity.ResourceManager.dll",
        "UnityEngine.CoreModule.dll",
        "UnityEngine.JSONSerializeModule.dll",
        "mscorlib.dll",
    };
    // }}

    // {{ constraint implement type
    // }} 

    // {{ AOT generic types
    // DelegateList<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>>
    // DelegateList<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle>
    // DelegateList<float>
    // System.Action<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Action<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Action<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Action<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Action<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Action<System.ValueTuple<object,int>>
    // System.Action<UnityEngine.RaycastHit2D>
    // System.Action<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle,object>
    // System.Action<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>>
    // System.Action<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle>
    // System.Action<byte>
    // System.Action<float>
    // System.Action<int,int,long,int>
    // System.Action<int>
    // System.Action<long>
    // System.Action<object,UnityEngine.Vector2>
    // System.Action<object,object,int>
    // System.Action<object,object>
    // System.Action<object>
    // System.Collections.Generic.ArraySortHelper<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.ArraySortHelper<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.ArraySortHelper<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.ArraySortHelper<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.ArraySortHelper<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.ArraySortHelper<System.ValueTuple<object,int>>
    // System.Collections.Generic.ArraySortHelper<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.ArraySortHelper<float>
    // System.Collections.Generic.ArraySortHelper<int>
    // System.Collections.Generic.ArraySortHelper<long>
    // System.Collections.Generic.ArraySortHelper<object>
    // System.Collections.Generic.Comparer<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.Comparer<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.Comparer<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.Comparer<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.Comparer<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.Comparer<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.Comparer<System.ValueTuple<object,int>>
    // System.Collections.Generic.Comparer<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.Comparer<float>
    // System.Collections.Generic.Comparer<int>
    // System.Collections.Generic.Comparer<long>
    // System.Collections.Generic.Comparer<object>
    // System.Collections.Generic.Comparer<ulong>
    // System.Collections.Generic.Dictionary.Enumerator<System.ValueTuple<int,int>,object>
    // System.Collections.Generic.Dictionary.Enumerator<Unity.Networking.Transport.NetworkConnection,object>
    // System.Collections.Generic.Dictionary.Enumerator<int,System.ValueTuple<int,ulong>>
    // System.Collections.Generic.Dictionary.Enumerator<int,int>
    // System.Collections.Generic.Dictionary.Enumerator<int,long>
    // System.Collections.Generic.Dictionary.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.Enumerator<long,int>
    // System.Collections.Generic.Dictionary.Enumerator<long,object>
    // System.Collections.Generic.Dictionary.Enumerator<object,byte>
    // System.Collections.Generic.Dictionary.Enumerator<object,float>
    // System.Collections.Generic.Dictionary.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<System.ValueTuple<int,int>,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<Unity.Networking.Transport.NetworkConnection,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,System.ValueTuple<int,ulong>>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,int>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,long>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<long,int>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<long,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,byte>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,float>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.KeyCollection<System.ValueTuple<int,int>,object>
    // System.Collections.Generic.Dictionary.KeyCollection<Unity.Networking.Transport.NetworkConnection,object>
    // System.Collections.Generic.Dictionary.KeyCollection<int,System.ValueTuple<int,ulong>>
    // System.Collections.Generic.Dictionary.KeyCollection<int,int>
    // System.Collections.Generic.Dictionary.KeyCollection<int,long>
    // System.Collections.Generic.Dictionary.KeyCollection<int,object>
    // System.Collections.Generic.Dictionary.KeyCollection<long,int>
    // System.Collections.Generic.Dictionary.KeyCollection<long,object>
    // System.Collections.Generic.Dictionary.KeyCollection<object,byte>
    // System.Collections.Generic.Dictionary.KeyCollection<object,float>
    // System.Collections.Generic.Dictionary.KeyCollection<object,int>
    // System.Collections.Generic.Dictionary.KeyCollection<object,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<System.ValueTuple<int,int>,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<Unity.Networking.Transport.NetworkConnection,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,System.ValueTuple<int,ulong>>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,int>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,long>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<long,int>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<long,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,byte>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,float>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.ValueCollection<System.ValueTuple<int,int>,object>
    // System.Collections.Generic.Dictionary.ValueCollection<Unity.Networking.Transport.NetworkConnection,object>
    // System.Collections.Generic.Dictionary.ValueCollection<int,System.ValueTuple<int,ulong>>
    // System.Collections.Generic.Dictionary.ValueCollection<int,int>
    // System.Collections.Generic.Dictionary.ValueCollection<int,long>
    // System.Collections.Generic.Dictionary.ValueCollection<int,object>
    // System.Collections.Generic.Dictionary.ValueCollection<long,int>
    // System.Collections.Generic.Dictionary.ValueCollection<long,object>
    // System.Collections.Generic.Dictionary.ValueCollection<object,byte>
    // System.Collections.Generic.Dictionary.ValueCollection<object,float>
    // System.Collections.Generic.Dictionary.ValueCollection<object,int>
    // System.Collections.Generic.Dictionary.ValueCollection<object,object>
    // System.Collections.Generic.Dictionary<System.ValueTuple<int,int>,object>
    // System.Collections.Generic.Dictionary<Unity.Networking.Transport.NetworkConnection,object>
    // System.Collections.Generic.Dictionary<int,System.ValueTuple<int,ulong>>
    // System.Collections.Generic.Dictionary<int,int>
    // System.Collections.Generic.Dictionary<int,long>
    // System.Collections.Generic.Dictionary<int,object>
    // System.Collections.Generic.Dictionary<long,int>
    // System.Collections.Generic.Dictionary<long,object>
    // System.Collections.Generic.Dictionary<object,byte>
    // System.Collections.Generic.Dictionary<object,float>
    // System.Collections.Generic.Dictionary<object,int>
    // System.Collections.Generic.Dictionary<object,object>
    // System.Collections.Generic.EqualityComparer<System.ValueTuple<int,int>>
    // System.Collections.Generic.EqualityComparer<System.ValueTuple<int,ulong>>
    // System.Collections.Generic.EqualityComparer<Unity.Networking.Transport.NetworkConnection>
    // System.Collections.Generic.EqualityComparer<byte>
    // System.Collections.Generic.EqualityComparer<float>
    // System.Collections.Generic.EqualityComparer<int>
    // System.Collections.Generic.EqualityComparer<long>
    // System.Collections.Generic.EqualityComparer<object>
    // System.Collections.Generic.EqualityComparer<ulong>
    // System.Collections.Generic.HashSet.Enumerator<System.ValueTuple<int,int>>
    // System.Collections.Generic.HashSet.Enumerator<int>
    // System.Collections.Generic.HashSet.Enumerator<long>
    // System.Collections.Generic.HashSet.Enumerator<object>
    // System.Collections.Generic.HashSet<System.ValueTuple<int,int>>
    // System.Collections.Generic.HashSet<int>
    // System.Collections.Generic.HashSet<long>
    // System.Collections.Generic.HashSet<object>
    // System.Collections.Generic.HashSetEqualityComparer<System.ValueTuple<int,int>>
    // System.Collections.Generic.HashSetEqualityComparer<int>
    // System.Collections.Generic.HashSetEqualityComparer<long>
    // System.Collections.Generic.HashSetEqualityComparer<object>
    // System.Collections.Generic.ICollection<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.ICollection<Rpg.Networking.PrimaryHitSegmentTiming>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<System.ValueTuple<int,int>,object>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<Unity.Networking.Transport.NetworkConnection,object>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,long>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<long,int>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,byte>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,float>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.ICollection<System.ValueTuple<int,int>>
    // System.Collections.Generic.ICollection<System.ValueTuple<object,int>>
    // System.Collections.Generic.ICollection<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.ICollection<float>
    // System.Collections.Generic.ICollection<int>
    // System.Collections.Generic.ICollection<long>
    // System.Collections.Generic.ICollection<object>
    // System.Collections.Generic.IComparer<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.IComparer<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.IComparer<System.ValueTuple<object,int>>
    // System.Collections.Generic.IComparer<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.IComparer<float>
    // System.Collections.Generic.IComparer<int>
    // System.Collections.Generic.IComparer<long>
    // System.Collections.Generic.IComparer<object>
    // System.Collections.Generic.IEnumerable<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.IEnumerable<Rpg.Networking.PrimaryHitSegmentTiming>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<System.ValueTuple<int,int>,object>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<Unity.Networking.Transport.NetworkConnection,object>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,long>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<long,int>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,byte>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,float>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.IEnumerable<System.ValueTuple<int,int>>
    // System.Collections.Generic.IEnumerable<System.ValueTuple<object,int>>
    // System.Collections.Generic.IEnumerable<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.IEnumerable<float>
    // System.Collections.Generic.IEnumerable<int>
    // System.Collections.Generic.IEnumerable<long>
    // System.Collections.Generic.IEnumerable<object>
    // System.Collections.Generic.IEnumerator<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.IEnumerator<Rpg.Networking.PrimaryHitSegmentTiming>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<System.ValueTuple<int,int>,object>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<Unity.Networking.Transport.NetworkConnection,object>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,long>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<long,int>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,byte>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,float>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.IEnumerator<System.ValueTuple<int,int>>
    // System.Collections.Generic.IEnumerator<System.ValueTuple<object,int>>
    // System.Collections.Generic.IEnumerator<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.IEnumerator<float>
    // System.Collections.Generic.IEnumerator<int>
    // System.Collections.Generic.IEnumerator<long>
    // System.Collections.Generic.IEnumerator<object>
    // System.Collections.Generic.IEqualityComparer<System.ValueTuple<int,int>>
    // System.Collections.Generic.IEqualityComparer<Unity.Networking.Transport.NetworkConnection>
    // System.Collections.Generic.IEqualityComparer<int>
    // System.Collections.Generic.IEqualityComparer<long>
    // System.Collections.Generic.IEqualityComparer<object>
    // System.Collections.Generic.IList<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.IList<Rpg.Networking.PrimaryHitSegmentTiming>
    // System.Collections.Generic.IList<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.IList<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.IList<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IList<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.IList<System.ValueTuple<object,int>>
    // System.Collections.Generic.IList<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.IList<float>
    // System.Collections.Generic.IList<int>
    // System.Collections.Generic.IList<long>
    // System.Collections.Generic.IList<object>
    // System.Collections.Generic.IReadOnlyCollection<Rpg.Networking.PrimaryHitSegmentTiming>
    // System.Collections.Generic.IReadOnlyCollection<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IReadOnlyCollection<object>
    // System.Collections.Generic.IReadOnlyDictionary<int,object>
    // System.Collections.Generic.IReadOnlyList<Rpg.Networking.PrimaryHitSegmentTiming>
    // System.Collections.Generic.IReadOnlyList<object>
    // System.Collections.Generic.KeyValuePair<System.ValueTuple<int,int>,object>
    // System.Collections.Generic.KeyValuePair<Unity.Networking.Transport.NetworkConnection,object>
    // System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>
    // System.Collections.Generic.KeyValuePair<int,int>
    // System.Collections.Generic.KeyValuePair<int,long>
    // System.Collections.Generic.KeyValuePair<int,object>
    // System.Collections.Generic.KeyValuePair<long,int>
    // System.Collections.Generic.KeyValuePair<long,object>
    // System.Collections.Generic.KeyValuePair<object,byte>
    // System.Collections.Generic.KeyValuePair<object,float>
    // System.Collections.Generic.KeyValuePair<object,int>
    // System.Collections.Generic.KeyValuePair<object,object>
    // System.Collections.Generic.LinkedList.Enumerator<object>
    // System.Collections.Generic.LinkedList<object>
    // System.Collections.Generic.LinkedListNode<object>
    // System.Collections.Generic.List.Enumerator<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.List.Enumerator<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.List.Enumerator<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.List.Enumerator<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.List.Enumerator<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.List.Enumerator<System.ValueTuple<object,int>>
    // System.Collections.Generic.List.Enumerator<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.List.Enumerator<float>
    // System.Collections.Generic.List.Enumerator<int>
    // System.Collections.Generic.List.Enumerator<long>
    // System.Collections.Generic.List.Enumerator<object>
    // System.Collections.Generic.List<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.List<System.ValueTuple<object,int>>
    // System.Collections.Generic.List<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.List<float>
    // System.Collections.Generic.List<int>
    // System.Collections.Generic.List<long>
    // System.Collections.Generic.List<object>
    // System.Collections.Generic.ObjectComparer<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.Generic.ObjectComparer<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.Generic.ObjectComparer<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.Generic.ObjectComparer<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.ObjectComparer<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.Generic.ObjectComparer<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.ObjectComparer<System.ValueTuple<object,int>>
    // System.Collections.Generic.ObjectComparer<UnityEngine.RaycastHit2D>
    // System.Collections.Generic.ObjectComparer<float>
    // System.Collections.Generic.ObjectComparer<int>
    // System.Collections.Generic.ObjectComparer<long>
    // System.Collections.Generic.ObjectComparer<object>
    // System.Collections.Generic.ObjectComparer<ulong>
    // System.Collections.Generic.ObjectEqualityComparer<System.ValueTuple<int,int>>
    // System.Collections.Generic.ObjectEqualityComparer<System.ValueTuple<int,ulong>>
    // System.Collections.Generic.ObjectEqualityComparer<Unity.Networking.Transport.NetworkConnection>
    // System.Collections.Generic.ObjectEqualityComparer<byte>
    // System.Collections.Generic.ObjectEqualityComparer<float>
    // System.Collections.Generic.ObjectEqualityComparer<int>
    // System.Collections.Generic.ObjectEqualityComparer<long>
    // System.Collections.Generic.ObjectEqualityComparer<object>
    // System.Collections.Generic.ObjectEqualityComparer<ulong>
    // System.Collections.Generic.Queue.Enumerator<Rpg.Networking.LocalMovementPrediction.InputRecord>
    // System.Collections.Generic.Queue.Enumerator<object>
    // System.Collections.Generic.Queue<Rpg.Networking.LocalMovementPrediction.InputRecord>
    // System.Collections.Generic.Queue<object>
    // System.Collections.Generic.SortedDictionary.<>c__DisplayClass34_0<object,object>
    // System.Collections.Generic.SortedDictionary.<>c__DisplayClass34_1<object,object>
    // System.Collections.Generic.SortedDictionary.Enumerator<object,object>
    // System.Collections.Generic.SortedDictionary.KeyCollection.<>c__DisplayClass5_0<object,object>
    // System.Collections.Generic.SortedDictionary.KeyCollection.<>c__DisplayClass6_0<object,object>
    // System.Collections.Generic.SortedDictionary.KeyCollection.Enumerator<object,object>
    // System.Collections.Generic.SortedDictionary.KeyCollection<object,object>
    // System.Collections.Generic.SortedDictionary.KeyValuePairComparer<object,object>
    // System.Collections.Generic.SortedDictionary.ValueCollection.<>c__DisplayClass5_0<object,object>
    // System.Collections.Generic.SortedDictionary.ValueCollection.<>c__DisplayClass6_0<object,object>
    // System.Collections.Generic.SortedDictionary.ValueCollection.Enumerator<object,object>
    // System.Collections.Generic.SortedDictionary.ValueCollection<object,object>
    // System.Collections.Generic.SortedDictionary<object,object>
    // System.Collections.Generic.SortedSet.<>c__DisplayClass52_0<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.SortedSet.<>c__DisplayClass53_0<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.SortedSet.Enumerator<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.SortedSet.Node<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.SortedSet<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.Stack.Enumerator<object>
    // System.Collections.Generic.Stack<object>
    // System.Collections.Generic.TreeSet<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.TreeWalkPredicate<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.ObjectModel.ReadOnlyCollection<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Collections.ObjectModel.ReadOnlyCollection<Rpg.Networking.PrimaryHitSegmentTiming>
    // System.Collections.ObjectModel.ReadOnlyCollection<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Collections.ObjectModel.ReadOnlyCollection<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Collections.ObjectModel.ReadOnlyCollection<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.ObjectModel.ReadOnlyCollection<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Collections.ObjectModel.ReadOnlyCollection<System.ValueTuple<object,int>>
    // System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.RaycastHit2D>
    // System.Collections.ObjectModel.ReadOnlyCollection<float>
    // System.Collections.ObjectModel.ReadOnlyCollection<int>
    // System.Collections.ObjectModel.ReadOnlyCollection<long>
    // System.Collections.ObjectModel.ReadOnlyCollection<object>
    // System.Comparison<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Comparison<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Comparison<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Comparison<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Comparison<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Comparison<System.ValueTuple<object,int>>
    // System.Comparison<UnityEngine.RaycastHit2D>
    // System.Comparison<float>
    // System.Comparison<int>
    // System.Comparison<long>
    // System.Comparison<object>
    // System.Converter<object,object>
    // System.Func<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Func<int>
    // System.Func<long>
    // System.Func<object,UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Func<object,byte>
    // System.Func<object,float,byte>
    // System.Func<object,object>
    // System.Func<object>
    // System.Linq.Enumerable.<OfTypeIterator>d__97<object>
    // System.Linq.Enumerable.Iterator<object>
    // System.Linq.Enumerable.WhereArrayIterator<object>
    // System.Linq.Enumerable.WhereEnumerableIterator<object>
    // System.Linq.Enumerable.WhereListIterator<object>
    // System.Nullable<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>>
    // System.Nullable<byte>
    // System.Nullable<int>
    // System.Nullable<long>
    // System.Predicate<Rpg.Networking.AuthorityPositionBuffer.Sample>
    // System.Predicate<System.Collections.Generic.KeyValuePair<int,System.ValueTuple<int,ulong>>>
    // System.Predicate<System.Collections.Generic.KeyValuePair<int,int>>
    // System.Predicate<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Predicate<System.Collections.Generic.KeyValuePair<long,object>>
    // System.Predicate<System.ValueTuple<int,int>>
    // System.Predicate<System.ValueTuple<object,int>>
    // System.Predicate<UnityEngine.RaycastHit2D>
    // System.Predicate<float>
    // System.Predicate<int>
    // System.Predicate<long>
    // System.Predicate<object>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<object>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable<object>
    // System.Runtime.CompilerServices.TaskAwaiter<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Runtime.CompilerServices.TaskAwaiter<object>
    // System.Threading.Tasks.Task<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Threading.Tasks.Task<object>
    // System.Threading.Tasks.TaskCompletionSource<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Threading.Tasks.TaskCompletionSource<object>
    // System.Threading.Tasks.TaskFactory<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // System.Threading.Tasks.TaskFactory<object>
    // System.Tuple<object,object,object>
    // System.ValueTuple<int,int>
    // System.ValueTuple<int,ulong>
    // System.ValueTuple<object,int>
    // UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene,int>
    // UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene>
    // UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationBase.<>c__DisplayClass60_0<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationBase.<>c__DisplayClass61_0<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationBase<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle.<>c<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
    // UnityEngine.ResourceManagement.Util.GlobalLinkedListNodeCache<object>
    // UnityEngine.ResourceManagement.Util.LinkedListNodeCache<object>
    // }}

    public void RefMethods()
    {
        // System.Collections.ObjectModel.ReadOnlyCollection<Rpg.Networking.PrimaryHitSegmentTiming> System.Array.AsReadOnly<Rpg.Networking.PrimaryHitSegmentTiming>(Rpg.Networking.PrimaryHitSegmentTiming[])
        // System.Collections.ObjectModel.ReadOnlyCollection<object> System.Array.AsReadOnly<object>(object[])
        // object[] System.Array.ConvertAll<object,object>(object[],System.Converter<object,object>)
        // Rpg.Networking.PrimaryHitSegmentTiming[] System.Array.Empty<Rpg.Networking.PrimaryHitSegmentTiming>()
        // object[] System.Array.Empty<object>()
        // bool System.Array.Exists<object>(object[],System.Predicate<object>)
        // int System.Array.FindIndex<object>(object[],System.Predicate<object>)
        // int System.Array.FindIndex<object>(object[],int,int,System.Predicate<object>)
        // int System.Array.IndexOf<object>(object[],object)
        // int System.Array.IndexOfImpl<object>(object[],object,int,int)
        // System.Void System.Array.Sort<float>(float[])
        // System.Void System.Array.Sort<float>(float[],int,int,System.Collections.Generic.IComparer<float>)
        // System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.OfType<object>(System.Collections.IEnumerable)
        // System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.OfTypeIterator<object>(System.Collections.IEnumerable)
        // System.Collections.Generic.List<object> System.Linq.Enumerable.ToList<object>(System.Collections.Generic.IEnumerable<object>)
        // System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Where<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
        // object& System.Runtime.CompilerServices.Unsafe.As<object,object>(object&)
        // System.Void* System.Runtime.CompilerServices.Unsafe.AsPointer<object>(object&)
        // System.Tuple<object,object,object> System.Tuple.Create<object,object,object>(object,object,object)
        // object UnityEngine.Component.GetComponent<object>()
        // object UnityEngine.Component.GetComponentInChildren<object>()
        // object UnityEngine.Component.GetComponentInChildren<object>(bool)
        // object UnityEngine.Component.GetComponentInParent<object>()
        // object UnityEngine.Component.GetComponentInParent<object>(bool)
        // object[] UnityEngine.Component.GetComponents<object>()
        // object[] UnityEngine.Component.GetComponentsInChildren<object>(bool)
        // object UnityEngine.GameObject.AddComponent<object>()
        // object UnityEngine.GameObject.GetComponent<object>()
        // object UnityEngine.GameObject.GetComponentInChildren<object>(bool)
        // object UnityEngine.GameObject.GetComponentInParent<object>()
        // object UnityEngine.GameObject.GetComponentInParent<object>(bool)
        // object[] UnityEngine.GameObject.GetComponents<object>()
        // object[] UnityEngine.GameObject.GetComponentsInChildren<object>(bool)
        // object UnityEngine.JsonUtility.FromJson<object>(string)
        // object[] UnityEngine.Object.FindObjectsByType<object>(UnityEngine.FindObjectsInactive,UnityEngine.FindObjectsSortMode)
        // object[] UnityEngine.Object.FindObjectsByType<object>(UnityEngine.FindObjectsSortMode)
        // object UnityEngine.Object.Instantiate<object>(object)
        // object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform)
        // object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform,bool)
        // object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Vector3,UnityEngine.Quaternion)
        // object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Vector3,UnityEngine.Quaternion,UnityEngine.Transform)
        // object[] UnityEngine.Resources.ConvertObjects<object>(UnityEngine.Object[])
        // object UnityEngine.ScriptableObject.CreateInstance<object>()
    }
}
