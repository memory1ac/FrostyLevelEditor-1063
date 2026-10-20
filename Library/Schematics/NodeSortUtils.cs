using LevelEditorPlugin.Controls;
using System;
using System.Collections.Generic;

namespace LevelEditorPlugin.Library.Schematics;

public class NodeSortUtils
{
    private List<BaseVisual> m_nodes;
    private List<WireVisual> m_wires;

    public NodeSortUtils(List<BaseVisual> inData, List<WireVisual> inWires)
    {
        m_nodes = inData;
        m_wires = inWires;
    }

    public void ApplyAutoLayout()
    {
        // Gather the nodes that feed into and out of each node
        Dictionary<BaseNodeVisual, List<BaseNodeVisual>> ancestors = [];
        Dictionary<BaseNodeVisual, List<BaseNodeVisual>> children = [];

        foreach (BaseVisual visual in m_nodes)
        {
            if (visual is BaseNodeVisual node)
            {
                ancestors.Add(node, []);
                children.Add(node, []);
            }
        }

        foreach (WireVisual wire in m_wires)
        {
            BaseNodeVisual source = wire.Source;
            BaseNodeVisual target = wire.Target;

            ancestors[target].Add(source);
            children[source].Add(target);
        }

        List<List<BaseNodeVisual>> columns = [];
        List<BaseNodeVisual> alreadyProcessed = [];

        int columnIdx = 1;
        columns.Add([]);

        foreach (BaseNodeVisual node in m_nodes)
        {
            if (ancestors[node].Count == 0 && children[node].Count == 0)
            {
                alreadyProcessed.Add(node);
                columns[0].Add(node);
                continue;
            }

            if (ancestors[node].Count == 0)
            {
                LayoutNodes(node, children, columns, alreadyProcessed, columnIdx);
            }
        }

        columnIdx = 1;
        foreach (BaseNodeVisual node in m_nodes)
        {
            if (!alreadyProcessed.Contains(node))
            {
                LayoutNodes(node, children, columns, alreadyProcessed, columnIdx);
            }
        }

        double x = 100.0;
        double width = 0.0;

        foreach (List<BaseNodeVisual> column in columns)
        {
            double y = 96.0;
            foreach (BaseNodeVisual node in column)
            {
                node.Rect.X = x - (x % 8);
                node.Rect.Y = y - (y % 8);

                node.Update();

                double curWidth = Math.Floor((node.Rect.Width + 7.0) / 8.0) * 8.0;
                double curHeight = Math.Floor((node.Rect.Height + 7.0) / 8.0) * 8.0;

                y += curHeight + 56.0;
                if (curWidth > width)
                {
                    width = curWidth;
                }
            }

            x += width + 60.0;
        }
    }

    private int LayoutNodes(BaseNodeVisual node, Dictionary<BaseNodeVisual, List<BaseNodeVisual>> children, List<List<BaseNodeVisual>> columns, List<BaseNodeVisual> alreadyProcessed, int column)
    {
        if (alreadyProcessed.Contains(node))
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Contains(node))
                    return i;
            }
        }

        alreadyProcessed.Add(node);
        if (columns.Count <= column)
            columns.Add([]);
        columns[column++].Add(node);

        int minimumColumn = 0;
        foreach (BaseNodeVisual child in children[node])
        {
            int tmp = LayoutNodes(child, children, columns, alreadyProcessed, column);
            if (tmp < minimumColumn || minimumColumn == 0)
                minimumColumn = tmp;
        }

        if (minimumColumn > (column + 1))
        {
            columns[column - 1].Remove(node);
            columns[minimumColumn - 1].Add(node);
        }

        return column;
    }
}