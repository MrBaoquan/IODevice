using Xunit;
using Moq;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// UndoRedoService 单元测试
    /// </summary>
    public class UndoRedoServiceTests
    {
        [Fact]
        public void Initial_State_Has_No_History()
        {
            var service = new UndoRedoService();

            Assert.False(service.CanUndo);
            Assert.False(service.CanRedo);
        }

        [Fact]
        public void Execute_Adds_To_History()
        {
            var service = new UndoRedoService();
            bool executed = false;

            service.Execute(
                new LambdaCommand("Test", () => executed = true, () => executed = false)
            );

            Assert.True(executed);
            Assert.True(service.CanUndo);
            Assert.False(service.CanRedo);
        }

        [Fact]
        public void Undo_Reverts_Last_Action()
        {
            var service = new UndoRedoService();
            int value = 0;

            service.Execute(new LambdaCommand("Set to 1", () => value = 1, () => value = 0));
            Assert.Equal(1, value);

            service.Undo();

            Assert.Equal(0, value);
            Assert.False(service.CanUndo);
            Assert.True(service.CanRedo);
        }

        [Fact]
        public void Redo_Reapplies_Undone_Action()
        {
            var service = new UndoRedoService();
            int value = 0;

            service.Execute(new LambdaCommand("Set to 1", () => value = 1, () => value = 0));
            service.Undo();
            service.Redo();

            Assert.Equal(1, value);
            Assert.True(service.CanUndo);
            Assert.False(service.CanRedo);
        }

        [Fact]
        public void New_Action_Clears_Redo_Stack()
        {
            var service = new UndoRedoService();
            int value = 0;

            service.Execute(new LambdaCommand("Set to 1", () => value = 1, () => value = 0));
            service.Undo();
            Assert.True(service.CanRedo);

            service.Execute(new LambdaCommand("Set to 2", () => value = 2, () => value = 0));

            Assert.False(service.CanRedo);
        }

        [Fact]
        public void StateChanged_Fires_On_Execute()
        {
            var service = new UndoRedoService();
            bool changed = false;
            service.StateChanged += () => changed = true;

            service.Execute(new LambdaCommand("Noop", () => { }, () => { }));

            Assert.True(changed);
        }

        [Fact]
        public void Clear_Resets_Both_Stacks()
        {
            var service = new UndoRedoService();
            service.Execute(new LambdaCommand("A", () => { }, () => { }));
            service.Execute(new LambdaCommand("B", () => { }, () => { }));
            service.Undo();

            service.Clear();

            Assert.False(service.CanUndo);
            Assert.False(service.CanRedo);
        }
    }
}
