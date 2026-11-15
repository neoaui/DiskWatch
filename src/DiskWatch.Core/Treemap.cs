namespace DiskWatch.Core;

public readonly record struct MapRect(int Index, double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
}

/// <summary>纯函数：Squarified 树图布局（Bruls 等人的算法），让每块尽量接近正方形，面积与权重成正比。</summary>
public static class Treemap
{
    public static IReadOnlyList<MapRect> Squarify(IReadOnlyList<double> weights, double x, double y, double width, double height)
    {
        var result = new List<MapRect>();
        if (!(width > 0) || !(height > 0)) return result;
        var items = weights.Select((w, i) => (Weight: w, Index: i)).Where(t => t.Weight > 0 && double.IsFinite(t.Weight)).OrderByDescending(t => t.Weight).ToList();
        double total = items.Sum(t => t.Weight);
        if (total <= 0) return result;
        double scale = width * height / total;
        var areas = items.Select(t => (Area: t.Weight * scale, t.Index)).ToList();
        int start = 0;
        while (start < areas.Count)
        {
            double side = Math.Min(width, height);
            int end = start; double rowSum = 0, worst = double.MaxValue;
            while (end < areas.Count)
            {
                double sum = rowSum + areas[end].Area;
                double ratio = Worst(sum, areas[start].Area, areas[end].Area, side);
                if (end > start && ratio > worst) break;
                worst = ratio; rowSum = sum; end++;
            }
            bool last = end == areas.Count;
            bool vertical = width >= height; // 宽大于高：本行竖着排在左侧
            double thickness = last ? (vertical ? width : height) : rowSum / side;
            double offset = 0;
            for (int k = start; k < end; k++)
            {
                double length = k == end - 1 ? side - offset : areas[k].Area / thickness;
                result.Add(vertical ? new MapRect(areas[k].Index, x, y + offset, thickness, Math.Max(0, length)) : new MapRect(areas[k].Index, x + offset, y, Math.Max(0, length), thickness));
                offset += length;
            }
            if (vertical) { x += thickness; width = Math.Max(0, width - thickness); } else { y += thickness; height = Math.Max(0, height - thickness); }
            start = end;
        }
        return result;
    }
    private static double Worst(double sum, double max, double min, double side)
    {
        double s2 = sum * sum, w2 = side * side;
        return Math.Max(w2 * max / s2, s2 / (w2 * min));
    }
}

/// <summary>占用地图中的一个节点：目录（可进入）或“此目录中的文件”（按分类）。</summary>
public sealed class MapNode
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public bool IsFolder { get; init; }
    public MapNode? Parent { get; init; }
    public Category Category { get; set; }
    public long Bytes { get; set; }
    public string? ProtectedReason { get; set; }
    public List<MapNode> Children { get; } = [];
}

/// <summary>纯函数：把快照的目录统计桶组成可逐层进入的树，用于占用地图。</summary>
public static class OccupancyMap
{
    public static MapNode Build(Snapshot snapshot)
    {
        var rootPath = Classifier.Normalize(snapshot.Root);
        var root = new MapNode { Path = snapshot.Root, Name = snapshot.Root, IsFolder = true };
        var folders = new Dictionary<string, MapNode>(StringComparer.OrdinalIgnoreCase) { [rootPath] = root };
        var own = new Dictionary<(MapNode Node, Category Category), long>();
        foreach (var bucket in snapshot.Buckets.Where(b => b.AllocatedBytes > 0))
        {
            var path = Classifier.Normalize(bucket.Path);
            var node = root;
            if (!path.Equals(rootPath, StringComparison.OrdinalIgnoreCase) && Classifier.Under(path, rootPath))
            {
                var current = rootPath;
                foreach (var segment in path[(rootPath.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
                {
                    current = current + "\\" + segment;
                    if (!folders.TryGetValue(current, out var child))
                    {
                        child = new MapNode { Path = current, Name = segment, IsFolder = true, Parent = node };
                        node.Children.Add(child); folders[current] = child;
                    }
                    node = child;
                }
            }
            own[(node, bucket.Category)] = own.GetValueOrDefault((node, bucket.Category)) + bucket.AllocatedBytes;
        }
        foreach (var ((node, category), bytes) in own)
            node.Children.Add(new MapNode { Path = node.Path, Name = "此目录中的文件 · " + Labels.CategoryName(category), Category = category, Bytes = bytes, Parent = node });
        Finish(root, true);
        return root;
    }

    private static Dictionary<Category, long> Finish(MapNode node, bool isRoot)
    {
        var totals = new Dictionary<Category, long>();
        if (!node.IsFolder) { totals[node.Category] = node.Bytes; }
        else
        {
            foreach (var child in node.Children)
                foreach (var (category, bytes) in Finish(child, false)) totals[category] = totals.GetValueOrDefault(category) + bytes;
            node.Bytes = totals.Values.Sum();
            node.Category = totals.Count == 0 ? Category.Other : totals.MaxBy(x => x.Value).Key;
            node.Children.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        }
        // 根目录本身不标红；目录按主要分类判断，文件块按自身分类判断。
        if (!isRoot) node.ProtectedReason = Cleanup.NotDeletableReason(node.Path, node.Category);
        return totals;
    }
}