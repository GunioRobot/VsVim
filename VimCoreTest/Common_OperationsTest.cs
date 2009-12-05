﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;
using VimCore.Modes.Common;
using Moq;
using VimCore;
using VimCoreTest.Utils;

namespace VimCoreTest
{
    [TestClass]
    public class Common_OperationsTest
    {
        private IWpfTextView _view;
        private ITextBuffer _buffer;

        public void CreateLines(params string[] lines)
        {
            _view = Utils.EditorUtil.CreateView(lines);
            _view.Caret.MoveTo(new SnapshotPoint(_view.TextSnapshot, 0));
            _buffer = _view.TextBuffer;
        }

        [TestMethod]
        public void Join1()
        {
            CreateLines("foo","bar");
            Assert.IsTrue(Operations.Join(_view, _view.GetCaretPoint(), 2));
            Assert.AreEqual("foo bar", _view.TextSnapshot.GetLineFromLineNumber(0).GetText());
            Assert.AreEqual(1, _view.TextSnapshot.LineCount);
            Assert.AreEqual(4, _view.Caret.Position.BufferPosition.Position);
        }

        [TestMethod,Description("Eat spaces at the start of the next line")]
        public void Join2()
        {
            CreateLines("foo", "   bar");
            Assert.IsTrue(Operations.Join(_view, _view.GetCaretPoint(), 2));
            Assert.AreEqual("foo bar", _view.TextSnapshot.GetLineFromLineNumber(0).GetText());
            Assert.AreEqual(1, _view.TextSnapshot.LineCount);
            Assert.AreEqual(4, _view.Caret.Position.BufferPosition.Position);
        }

        [TestMethod, Description("Join with a count")]
        public void Join3()
        {
            CreateLines("foo", "bar", "baz");
            Assert.IsTrue(Operations.Join(_view, _view.GetCaretPoint(), 3));
            Assert.AreEqual("foo bar baz", _view.TextSnapshot.GetLineFromLineNumber(0).GetText());
            Assert.AreEqual(1, _view.TextSnapshot.LineCount);
            Assert.AreEqual(8, _view.Caret.Position.BufferPosition.Position);
        }

        [TestMethod, Description("Join with a single count, should be no different")]
        public void Join4()
        {
            CreateLines("foo", "bar");
            Assert.IsTrue(Operations.Join(_view, _view.GetCaretPoint(), 1));
            Assert.AreEqual("foo bar", _view.TextSnapshot.GetLineFromLineNumber(0).GetText());
            Assert.AreEqual(1, _view.TextSnapshot.LineCount);
            Assert.AreEqual(4, _view.Caret.Position.BufferPosition.Position);
        }

        [TestMethod]
        public void GoToDefinition1()
        {
            CreateLines("foo");
            var host = new Mock<IVimHost>(MockBehavior.Strict);
            host.Setup(x => x.GoToDefinition()).Returns(true);
            var res = Operations.GoToDefinition(_view, host.Object);
            Assert.IsTrue(res.IsSucceeded);
        }

        [TestMethod]
        public void GoToDefinition2()
        {
            CreateLines("foo");
            var host = new Mock<IVimHost>(MockBehavior.Strict);
            host.Setup(x => x.GoToDefinition()).Returns(false);
            var res = Operations.GoToDefinition(_view, host.Object);
            Assert.IsTrue(res.IsFailed);
            Assert.IsTrue(((Operations.Result.Failed)res).Item.Contains("foo"));
        }

        [TestMethod, Description("Make sure we don't crash when nothing is under the cursor")]
        public void GoToDefinition3()
        {
            CreateLines("      foo");
            var host = new Mock<IVimHost>(MockBehavior.Strict);
            host.Setup(x => x.GoToDefinition()).Returns(false);
            var res = Operations.GoToDefinition(_view, host.Object);
            Assert.IsTrue(res.IsFailed);
        }

        [TestMethod]
        public void SetMark1()
        {
            CreateLines("foo");
            var map = new MarkMap();
            var res = Operations.SetMark(map, _buffer.CurrentSnapshot.GetLineFromLineNumber(0).Start, 'a');
            Assert.IsTrue(res.IsSucceeded);
            Assert.IsTrue(map.GetLocalMark(_buffer, 'a').IsSome());
        }

        [TestMethod,Description("Invalid mark character")]
        public void SetMark2()
        {
            CreateLines("bar");
            var map = new MarkMap();
            var res = Operations.SetMark(map, _buffer.CurrentSnapshot.GetLineFromLineNumber(0).Start, ';');
            Assert.IsTrue(res.IsFailed);
        }

        [TestMethod]
        public void JumpToMark1()
        {
            var view = Utils.EditorUtil.CreateView("foo", "bar");
            var map = new MarkMap();
            map.SetMark(new SnapshotPoint(view.TextSnapshot, 0), 'a');
            var res = Operations.JumpToMark(map, view, 'a');
            Assert.IsTrue(res.IsSucceeded);
        }

        [TestMethod]
        public void JumpToMark2()
        {
            var view = Utils.EditorUtil.CreateView("foo", "bar");
            var map = new MarkMap();
            var res = Operations.JumpToMark(map, view, 'b');
            Assert.IsTrue(res.IsFailed);
        }

        [TestMethod, Description("Global marks aren't supported yet")]
        public void JumpToMark3()
        {
            var view = Utils.EditorUtil.CreateView("foo", "bar");
            var map = new MarkMap();
            map.SetMark(new SnapshotPoint(view.TextSnapshot, 0), 'B');
            var res = Operations.JumpToMark(map, view, 'B');
            Assert.IsTrue(res.IsFailed);
        }
    }
}