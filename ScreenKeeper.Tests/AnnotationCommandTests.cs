using ScreenKeeper.Models;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.Tests;

public class AnnotationCommandTests {
    private static CaptureDocument createDocument() {
        return new CaptureDocument {
            Image = "test.png",
            VirtualScreen = new PixelRect(0, 0, 800, 600),
            Annotations = new List<Annotation> { new CursorAnnotation { X = 5, Y = 5 } }
        };
    }

    private static string json(CaptureDocument document) {
        return CaptureDocumentSerializer.Serialize(document);
    }

    [Fact]
    public void Add_UndoRedo_UsesCopies() {
        CaptureDocument document = createDocument();
        RectangleAnnotation rectangle = new RectangleAnnotation { Rect = new PixelRect(1, 2, 3, 4) };
        UndoStack stack = new UndoStack();
        stack.Execute(new AddAnnotationCommand(document, rectangle));
        Assert.Equal(2, document.Annotations.Count);
        Assert.Equal(rectangle.Id, document.Annotations[1].Id);
        // spätere Änderung am übergebenen Objekt verfälscht den Verlauf nicht
        rectangle.Rect = new PixelRect(9, 9, 9, 9);
        stack.Undo();
        Assert.Single(document.Annotations);
        stack.Redo();
        Assert.Equal(new PixelRect(1, 2, 3, 4), ((RectangleAnnotation)document.Annotations[1]).Rect);
    }

    [Fact]
    public void Add_AtIndex_And_Remove_RestoresDrawingOrder() {
        CaptureDocument document = createDocument();
        UndoStack stack = new UndoStack();
        LineAnnotation first = new LineAnnotation();
        LineAnnotation second = new LineAnnotation();
        stack.Execute(new AddAnnotationCommand(document, first));
        stack.Execute(new AddAnnotationCommand(document, second, 1));
        Assert.Equal(new[] { AnnotationTypes.Cursor, AnnotationTypes.Line, AnnotationTypes.Line }, document.Annotations.Select(a => a.Type));
        Assert.Equal(second.Id, document.Annotations[1].Id);

        stack.Execute(new RemoveAnnotationCommand(document, document.Annotations[1]));
        Assert.Equal(first.Id, document.Annotations[1].Id);
        stack.Undo();
        Assert.Equal(second.Id, document.Annotations[1].Id);
        Assert.Equal(first.Id, document.Annotations[2].Id);
    }

    [Fact]
    public void Change_ReplacesById_BothDirections() {
        CaptureDocument document = createDocument();
        NumberStampAnnotation stamp = new NumberStampAnnotation { X = 10, Y = 10, Number = 3 };
        document.Annotations.Add(stamp);
        NumberStampAnnotation after = (NumberStampAnnotation)AnnotationEditing.Moved(stamp, 5, 6);
        after.Size = 50;
        UndoStack stack = new UndoStack();
        stack.Execute(new ChangeAnnotationCommand(document, stamp, after, "Test"));
        NumberStampAnnotation current = (NumberStampAnnotation)document.Annotations[1];
        Assert.Equal((15, 16, 50.0, 3), (current.X, current.Y, current.Size, current.Number));
        Assert.Equal("Test", stack.UndoDescription);
        stack.Undo();
        current = (NumberStampAnnotation)document.Annotations[1];
        Assert.Equal((10, 10, 32.0), (current.X, current.Y, current.Size));
        Assert.Throws<ArgumentException>(() => new ChangeAnnotationCommand(document, stamp, new NumberStampAnnotation(), null));
    }

    [Fact]
    public void SixtySteps_UndoAll_RedoAll_DocumentIdentical() {
        CaptureDocument document = createDocument();
        UndoStack stack = new UndoStack();
        List<string> states = new List<string> { json(document) };
        Random random = new Random(42);
        for(int step = 0; step < 60; step++) {
            List<Annotation> existing = document.Annotations.Where(a => a is not CursorAnnotation).ToList();
            int kind = existing.Count == 0 ? 0 : step % 5;
            switch(kind) {
                case 0:
                case 3:
                    stack.Execute(new AddAnnotationCommand(document, new RectangleAnnotation { Rect = new PixelRect(random.Next(500), random.Next(400), 20, 10) }));
                    break;
                case 1: {
                    Annotation target = existing[random.Next(existing.Count)];
                    stack.Execute(new ChangeAnnotationCommand(document, target, AnnotationEditing.Moved(target, 3, 4), "move"));
                    break;
                }
                case 2: {
                    Annotation target = existing[random.Next(existing.Count)];
                    RectangleAnnotation changed = (RectangleAnnotation)target.Clone();
                    changed.Stroke = "#FF00FF00";
                    changed.Thickness = step;
                    stack.Execute(new ChangeAnnotationCommand(document, target, changed));
                    break;
                }
                case 4:
                    stack.Execute(new RemoveAnnotationCommand(document, existing[random.Next(existing.Count)]));
                    break;
            }
            states.Add(json(document));
        }
        Assert.Equal(60, stack.UndoCount);
        for(int i = 60; i > 0; i--) {
            Assert.True(stack.Undo());
            Assert.Equal(states[i - 1], json(document));
        }
        Assert.False(stack.CanUndo);
        for(int i = 1; i <= 60; i++) {
            Assert.True(stack.Redo());
            Assert.Equal(states[i], json(document));
        }
    }

    [Fact]
    public void Clone_IsDeepAndKeepsId() {
        FreehandAnnotation original = new FreehandAnnotation { Points = new List<PixelPoint> { new PixelPoint(1, 1) }, Smooth = false };
        FreehandAnnotation copy = (FreehandAnnotation)original.Clone();
        Assert.Equal(original.Id, copy.Id);
        Assert.NotSame(original.Points, copy.Points);
        copy.Points.Add(new PixelPoint(2, 2));
        Assert.Single(original.Points);
        Assert.False(copy.Smooth);
        Assert.Equal(original.ToJson(), ((FreehandAnnotation)original.Clone()).ToJson());
    }
}
