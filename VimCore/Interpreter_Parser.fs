﻿#light

namespace Vim.Interpreter
open Vim

[<RequireQualifiedAccess>]
type ParseResult<'T> = 
    | Succeeded of 'T
    | Failed of string

type ParseLineCommand = LineRange option -> ParseResult<LineCommand>

[<Sealed>]
type Parser
    (
        _text : string
    ) = 

    /// The set of supported line commands paired with their abbreviation
    static let s_LineCommandNamePair = [
        ("close", "clo")
        ("delete","d")
        ("edit", "e")
        ("exit", "exi")
        ("display","di")
        ("fold", "fo")
        ("join", "j")
        ("make", "mak")
        ("marks", "")
        ("nohlsearch", "noh")
        ("put", "pu")
        ("quit", "q")
        ("qall", "qa")
        ("quitall", "quita")
        ("redo", "red")
        ("registers", "reg")
        ("retab", "ret")
        ("set", "se")
        ("source","so")
        ("split", "sp")
        ("substitute", "s")
        ("smagic", "sm")
        ("snomagic", "sno")
        ("tabfirst", "tabfir")
        ("tablast", "tabl")
        ("tabnext", "tabn")
        ("tabNext", "tabN")
        ("tabprevious", "tabp")
        ("tabrewind", "tabr")
        ("undo", "u")
        ("write","w")
        ("wq", "")
        ("wall", "wa")
        ("xit", "x")
        ("yank", "y")
        ("/", "")
        ("?", "")
        ("<", "")
        (">", "")
        ("&", "&")
        ("~", "~")
        ("mapclear", "mapc")
        ("nmapclear", "nmapc")
        ("vmapclear", "vmapc")
        ("xmapclear", "xmapc")
        ("smapclear", "smapc")
        ("omapclear", "omapc")
        ("imapclear", "imapc")
        ("cmapclear", "cmapc")
        ("unmap", "unm")
        ("nunmap", "nun")
        ("vunmap", "vu")
        ("xunmap", "xu")
        ("sunmap", "sunm")
        ("ounmap", "ou")
        ("iunmap", "iu")
        ("lunmap", "lu")
        ("cunmap", "cu")
        ("map", "")
        ("nmap", "nm")
        ("vmap", "vm")
        ("xmap", "xm")
        ("smap", "")
        ("omap", "om")
        ("imap", "im")
        ("lmap", "lm")
        ("cmap", "cm")
        ("noremap", "no")
        ("nnoremap", "nn")
        ("vnoremap", "vn")
        ("xnoremap", "xn")
        ("snoremap", "snor")
        ("onoremap", "ono")
        ("inoremap", "ino")
        ("lnoremap", "ln")
        ("cnoremap", "cno")
    ]

    /// Current index into the expression text
    let mutable _index = 0

    member x.CurrentChar =
        if _index >= _text.Length then
            None
        else
            Some _text.[_index]

    /// Is the parser at the end of the line
    member x.IsAtEndOfLine = _index = _text.Length

    member x.IsCurrentChar predicate = 
        match x.CurrentChar with
        | None -> false
        | Some c -> predicate c

    member x.IsCurrentCharValue value =
        match x.CurrentChar with
        | None -> false
        | Some c -> c = value

    member x.RemainingText =
        _text.Substring(_index)

    member x.IncrementIndex() =
        if _index < _text.Length then
            _index <- _index + 1

    /// Move past the white space in the expression text
    member x.SkipBlanks () = 
        if x.IsCurrentChar CharUtil.IsBlank then
            x.IncrementIndex()
            x.SkipBlanks()
        else
            ()

    /// Try and expand the possible abbreviation to a full line command name.  If it's 
    /// not an abbreviation then the original string will be returned
    member x.TryExpand name =

        // Is 'name' an abbreviation of the given command name and abbreviation
        let isAbbreviation (fullName : string) (abbreviation : string) = 
            if name = fullName then
                true
            else 
                name.StartsWith(abbreviation) && fullName.StartsWith(name)

        s_LineCommandNamePair
        |> Seq.filter (fun (name, abbreviation) -> isAbbreviation name abbreviation)
        |> Seq.map fst
        |> SeqUtil.headOrDefault name

    /// Try and parse out the given word from the text.  If the next word matches the
    /// given string then the parser moves past that word and returns true.  Else the 
    /// index is unchanged and false is returned
    member x.TryParseWord word = 
        let mark = _index
        match x.ParseWord() with
        | None ->
            false
        | Some foundWord -> 
            if foundWord = word then
                true
            else
                _index <- mark
                false

    /// Parse out the '!'.  Returns true if a ! was found and consumed
    /// actually skipped
    member x.ParseBang () = 
        if x.IsCurrentChar (fun c -> c = '!') then
            x.IncrementIndex()
            true
        else
            false


    /// Parse out a single word from the text.  This will simply take the current cursor
    /// position and move while IsLetter is true.  This will return None if the resulting
    /// string is blank.  This will not skip any blanks
    member x.ParseWord () = 
        if x.IsCurrentChar CharUtil.IsNotBlank then
            let startIndex = _index
            x.IncrementIndex()
            let length = 
                let rec inner () = 
                    if x.IsCurrentChar CharUtil.IsNotBlank then
                        x.IncrementIndex()
                        inner ()
                inner()
                _index - startIndex
            let text = _text.Substring(startIndex, length)
            Some text
        else
            None

    /// Parse out a key notation argument
    member x.ParseKeyNotation() = 
        // TODO: Right now this lines up with parse word since they both go with non-blanks.  Keeping
        // separate as I believe ParseWord will need to evolve to be more specific
        x.ParseWord()

    /// Parse out the mapclear variants. 
    member x.ParseMapClear allowBang keyRemapModes =
        if x.ParseBang() then
            if allowBang then
                LineCommand.ClearKeyMap ([KeyRemapMode.Insert; KeyRemapMode.Command]) |> ParseResult.Succeeded
            else
                ParseResult.Failed Resources.Parser_NoBangAllowed
        else
            LineCommand.ClearKeyMap keyRemapModes |> ParseResult.Succeeded

    /// Parse out core portion of key mappings.
    member x.ParseMapKeysCore keyRemapModes allowRemap =

        match x.ParseKeyNotation() with
        | None -> 
            LineCommand.DisplayKeyMap (keyRemapModes, None) |> ParseResult.Succeeded
        | Some leftKeyNotation -> 
            x.SkipBlanks()
            match x.ParseKeyNotation() with
            | None ->
                LineCommand.DisplayKeyMap (keyRemapModes, Some leftKeyNotation) |> ParseResult.Succeeded
            | Some rightKeyNotation ->
                LineCommand.MapKeys (leftKeyNotation, rightKeyNotation, keyRemapModes, allowRemap) |> ParseResult.Succeeded

    /// Parse out the :map commands
    member x.ParseMapKeys allowBang keyRemapModes =

        if x.ParseBang() then
            if allowBang then
                x.ParseMapKeysCore [KeyRemapMode.Insert; KeyRemapMode.Command] true
            else
                ParseResult.Failed Resources.Parser_NoBangAllowed
        else
            x.ParseMapKeysCore keyRemapModes true

    /// Parse out the :nomap commands
    member x.ParseMapKeysNoRemap allowBang keyRemapModes =

        if x.ParseBang() then
            if allowBang then
                x.ParseMapKeysCore [KeyRemapMode.Insert; KeyRemapMode.Command] false
            else
                ParseResult.Failed Resources.Parser_NoBangAllowed
        else
            x.ParseMapKeysCore keyRemapModes false

    /// Parse out the unmap variants. 
    member x.ParseMapUnmap allowBang keyRemapModes =

        let inner modes = 
            match x.ParseKeyNotation() with
            | None -> ParseResult.Failed Resources.Parser_InvalidArgument
            | Some keyNotation -> LineCommand.UnmapKeys (keyNotation, modes) |> ParseResult.Succeeded

        if x.ParseBang() then
            if allowBang then
                inner [KeyRemapMode.Insert; KeyRemapMode.Command]
            else
                ParseResult.Failed Resources.Parser_NoBangAllowed
        else
            inner keyRemapModes

    /// Parse out a number from the current text
    member x.ParseNumber () = 

        // If c is a digit char then return back the digit
        let toDigit c = 
            if CharUtil.IsDigit c then
                (int c) - (int '0') |> Some
            else
                None

        // Get the current char as a digit if it is one
        let currentAsChar () = 
            match x.CurrentChar with
            | None -> None
            | Some c -> toDigit c

        let rec inner value = 
            match currentAsChar() with
            | None -> 
                value
            | Some number ->
                let value = (value * 10) + number
                x.IncrementIndex()
                inner value

        match currentAsChar() with
        | None -> 
            None
        | Some number -> 
            x.IncrementIndex()
            inner number |> Some

    /// Parse out the rest of the text to the end of the line 
    member x.ParseToEndOfLine() =
        let text = x.RemainingText
        _index <- _text.Length
        text

    /// Parse out a CommandOption value if the caret is currently pointed at one.  If 
    /// there is no CommnadOption here then the index will not change
    member x.ParseCommandOption () = 
        if x.IsCurrentCharValue '+' then
            let mark = _index

            x.IncrementIndex()
            match x.CurrentChar with
            | None ->
                // At the end of the line so it's just a '+' option
                CommandOption.StartAtLastLine |> Some
            | Some c ->
                if CharUtil.IsDigit c then
                    let number = x.ParseNumber() |> Option.get
                    CommandOption.StartAtLine number |> Some
                elif c = '/' then
                    x.IncrementIndex()
                    let pattern = x.ParseToEndOfLine()
                    CommandOption.StartAtPattern pattern |> Some
                else
                    match x.ParseSingleCommand() with
                    | ParseResult.Failed _ -> 
                        _index <- mark
                        None
                    | ParseResult.Succeeded lineCommand ->
                        CommandOption.ExecuteLineCommand lineCommand |> Some
        else
            None

    /// Parse out the '++opt' paramter to some commands.
    member x.ParseFileOptions () : FileOption list =

        // TODO: Need to implement parsing out FileOption list
        List.empty

    /// Parse out a register value from the text
    member x.ParseRegisterName () = 

        let name = 
            x.CurrentChar
            |> OptionUtil.map2 RegisterName.OfChar

        if Option.isSome name then
            x.IncrementIndex()

        name

    /// Used to parse out the flags for substitute commands.  Will not modify the 
    /// stream if there are no flags
    member x.ParseSubstituteFlags () =

        let rec inner flags = 
            match x.CurrentChar with
            | None -> flags
            | Some c ->
                let newFlag = 
                    match c with 
                    | 'c' -> Some SubstituteFlags.Confirm
                    | 'r' -> Some SubstituteFlags.UsePreviousSearchPattern
                    | 'e' -> Some SubstituteFlags.SuppressError
                    | 'g' -> Some SubstituteFlags.ReplaceAll
                    | 'i' -> Some SubstituteFlags.IgnoreCase
                    | 'I' -> Some SubstituteFlags.OrdinalCase
                    | 'n' -> Some SubstituteFlags.ReportOnly
                    | 'p' -> Some SubstituteFlags.PrintLast
                    | 'l' -> Some SubstituteFlags.PrintLastWithList
                    | '#' -> Some SubstituteFlags.PrintLastWithNumber
                    | '&' -> Some SubstituteFlags.UsePreviousFlags
                    | _  -> None
                match newFlag with
                | None -> 
                    // No more flags so we are done
                    flags
                | Some newFlag -> 
                    x.IncrementIndex()
                    inner (flags ||| newFlag)

        inner SubstituteFlags.None

    /// Parse out the :close command
    member x.ParseClose() = 
        let isBang = x.ParseBang()
        LineCommand.Close isBang |> ParseResult.Succeeded

    /// Parse out the :delete command
    member x.ParseDelete lineRange = 
        x.SkipBlanks()
        let name = x.ParseRegisterName()
        x.SkipBlanks()
        let count = x.ParseNumber()
        LineCommand.Delete (lineRange, name, count) |> ParseResult.Succeeded

    /// Parse out the :edit command
    member x.ParseEdit () = 
        let hasBang = x.ParseBang()

        x.SkipBlanks()
        let fileOptionList = x.ParseFileOptions()

        x.SkipBlanks()
        let commandOption = x.ParseCommandOption()

        x.SkipBlanks()
        let fileName = x.ParseToEndOfLine()

        LineCommand.Edit (hasBang, fileOptionList, commandOption, fileName)

    /// Parse out the :[digit] command
    member x.ParseJumpToLine () =
        match x.ParseNumber() with
        | None -> ParseResult.Failed Resources.Parser_Error
        | Some number -> ParseResult.Succeeded (LineCommand.JumpToLine number)

    /// Parse out the :$ command
    member x.ParseJumpToLastLine() =
        ParseResult.Succeeded (LineCommand.JumpToLastLine)

    /// Parse out a single char from the text
    member x.ParseChar() = 
        match x.CurrentChar with
        | None -> 
            None
        | Some c -> 
            x.IncrementIndex()
            Some c

    /// Parse a {pattern} out of the text.  The text will be consumed until the unescaped value 
    /// 'delimiter' is provided.   This method should be called with the index one past the start
    /// delimiter of the pattern
    member x.ParsePattern delimiter = 
        let mark = _index
        let rec inner () = 
            match x.CurrentChar with
            | None -> 
                // Hit the end without finding 'delimiter' so there is no pattern
                _index <- mark
                None 
            | Some c -> 
                if c = delimiter then 
                    let text = _text.Substring(mark, _index - mark)
                    x.IncrementIndex()
                    Some text
                elif c = '\\' then
                    x.IncrementIndex()
                    x.IncrementIndex()
                    inner()
                else
                    x.IncrementIndex()
                    inner()

        inner ()

    /// Parse out a LineSpecifier from the text.
    ///
    /// If there is no valid line specifier at the given place in the text then the 
    /// index should not be adjusted
    member x.ParseLineSpecifier () =

        let lineSpecifier = 
            if x.IsCurrentCharValue '.' then
                x.IncrementIndex()
                Some LineSpecifier.CurrentLine
            elif x.IsCurrentCharValue '\'' then
                x.IncrementIndex()
                x.ParseChar() 
                |> OptionUtil.map2 Mark.OfChar
                |> Option.map LineSpecifier.MarkLine
            elif x.IsCurrentCharValue '$' || x.IsCurrentCharValue '%' then
                x.IncrementIndex()
                Some LineSpecifier.LastLine
            elif x.IsCurrentCharValue '/' then

                // It's one of the forward pattern specifiers
                x.IncrementIndex()
                if x.IsCurrentCharValue '/' then
                    Some LineSpecifier.NextLineWithPreviousPattern
                elif x.IsCurrentCharValue '?' then
                    Some LineSpecifier.PreviousLineWithPreviousPattern
                elif x.IsCurrentCharValue '&' then
                    Some LineSpecifier.NextLineWithPreviousSubstitutePattern
                else
                    match x.ParsePattern '/' with
                    | None ->
                        None
                    | Some pattern -> 
                        Some (LineSpecifier.NextLineWithPattern pattern)

            elif x.IsCurrentCharValue '?' then
                // It's the ? previous search pattern
                x.IncrementIndex()
                match x.ParsePattern '?' with
                | None -> 
                    None
                | Some pattern ->
                    Some (LineSpecifier.PreviousLineWithPattern pattern)

            elif x.IsCurrentCharValue '+' then
                x.IncrementIndex()
                x.ParseNumber() |> Option.map LineSpecifier.AdjustmentOnCurrent
            elif x.IsCurrentCharValue '-' then
                x.IncrementIndex()
                x.ParseNumber() |> Option.map (fun number -> LineSpecifier.AdjustmentOnCurrent -number)
            else 
                match x.ParseNumber() with
                | None -> None
                | Some number -> Some (LineSpecifier.Number number)

        // Need to check for a trailing + or - 
        match lineSpecifier with
        | None ->
            None
        | Some lineSpecifier ->
            let parseAdjustment isNegative = 
                x.IncrementIndex()

                // If no number is specified then 1 is used instead
                let number = x.ParseNumber() |> OptionUtil.getOrDefault 1
                let number = 
                    if isNegative then
                        -number
                    else
                        number

                Some (LineSpecifier.LineSpecifierWithAdjustment (lineSpecifier, number))
            if x.IsCurrentCharValue '+' then
                parseAdjustment false
            elif x.IsCurrentCharValue '-' then
                parseAdjustment true
            else
                Some lineSpecifier

    /// Parse out any valid range node.  This will consider % and any other 
    /// range expression
    member x.ParseLineRange () =
        if x.IsCurrentCharValue '%' then
            x.IncrementIndex()
            LineRange.EntireBuffer |> Some
        else
            match x.ParseLineSpecifier() with
            | None ->
                None
            | Some left ->
                if x.IsCurrentCharValue ',' then
                    x.IncrementIndex()
                    x.ParseLineSpecifier()
                    |> Option.map (fun right -> LineRange.Range (left, right, false))
                elif x.IsCurrentCharValue ';' then
                    x.IncrementIndex()
                    x.ParseLineSpecifier()
                    |> Option.map (fun right -> LineRange.Range (left, right, true))
                else
                    LineRange.SingleLine left |> Some

    /// Parse out the substitute command.  This should be called with the index just after
    /// the end of the :substitute word
    member x.ParseSubstitute lineRange processFlags = 
        x.SkipBlanks()

        // Is this valid as a search string delimiter
        let isValidDelimiter c = 
            let isBad = 
                CharUtil.IsLetter c ||
                CharUtil.IsDigit c ||
                c = '\\' ||
                c = '"' ||
                c = '|'
            not isBad

        // Need to look at the next char to know if we are parsing out a search string or not for
        // this particular :substitute command
        if x.IsCurrentChar isValidDelimiter then
            // If this is a valid delimiter then first try and parse out the pattern version
            // of substitute 
            let delimiter = Option.get x.CurrentChar
            x.IncrementIndex()
            match x.ParsePattern delimiter with
            | None -> ParseResult.Failed Resources.Parser_Error
            | Some pattern ->
                match x.ParsePattern delimiter with
                | None -> ParseResult.Failed Resources.Parser_Error
                | Some replace ->
                    x.SkipBlanks()
                    let flags = x.ParseSubstituteFlags()
                    let flags = processFlags flags
                    x.SkipBlanks()
                    let count = x.ParseNumber()
                    let command = LineCommand.Substitute (lineRange, pattern, replace, flags, count)
                    ParseResult.Succeeded command
        else
            ParseResult.Failed Resources.Parser_Error

    /// Parse out the :smagic command
    member x.ParseSubstituteMagic lineRange = 
        x.ParseSubstitute lineRange (fun flags ->
            let flags = Util.UnsetFlag flags SubstituteFlags.Nomagic
            flags ||| SubstituteFlags.Magic)

    /// Parse out the :snomagic command
    member x.ParseSubstituteNoMagic lineRange = 
        x.ParseSubstitute lineRange (fun flags ->
            let flags = Util.UnsetFlag flags SubstituteFlags.Magic
            flags ||| SubstituteFlags.Nomagic)

    /// Parse out the '&' command
    member x.ParseSubstituteRepeatLast lineRange = 
        let flags = x.ParseSubstituteFlags()
        x.SkipBlanks()
        let count = x.ParseNumber()
        LineCommand.SubstituteRepeatLast (lineRange, flags, count) |> ParseResult.Succeeded

    /// Parse out the '~' command
    member x.ParseSubstituteRepeatLastWithSearch lineRange = 
        let flags = x.ParseSubstituteFlags()
        x.SkipBlanks()
        let count = x.ParseNumber()
        LineCommand.SubstituteRepeatLastWithSearch (lineRange, flags, count) |> ParseResult.Succeeded


    /// Parse out the search commands
    member x.ParseSearch path =
        let pattern = x.ParseToEndOfLine()
        LineCommand.Search (path, pattern) |> ParseResult.Succeeded

    /// Parse out the shift left pattern
    member x.ParseShiftLeft lineRange = 
        x.SkipBlanks()
        let count = x.ParseNumber()
        LineCommand.ShiftLeft (lineRange, count) |> ParseResult.Succeeded

    /// Parse out the shift right pattern
    member x.ParseShiftRight lineRange = 
        x.SkipBlanks()
        let count = x.ParseNumber()
        LineCommand.ShiftRight (lineRange, count) |> ParseResult.Succeeded

    /// Parse out the 'tabnext' command
    member x.ParseTabNext() =   
        x.SkipBlanks()
        let count = x.ParseNumber()
        ParseResult.Succeeded (LineCommand.GotoNextTab count)

    /// Parse out the 'tabprevious' command
    member x.ParseTabPrevious() =   
        x.SkipBlanks()
        let count = x.ParseNumber()
        ParseResult.Succeeded (LineCommand.GotoPreviousTab count)

    /// Parse out the quit and write command.  This includes 'wq', 'xit' and 'exit' commands.
    member x.ParseQuitAndWrite lineRange = 
        let hasBang = x.ParseBang()

        x.SkipBlanks()
        let fileOptionList = x.ParseFileOptions()

        x.SkipBlanks()
        let fileName =
            match x.CurrentChar with
            | None -> None
            | Some _ -> x.ParseToEndOfLine() |> Some

        LineCommand.QuitWithWrite (lineRange, hasBang, fileOptionList, fileName) |> ParseResult.Succeeded

    /// Parse out the yank command
    member x.ParseYank lineRange =
        x.SkipBlanks()
        let registerName = x.ParseRegisterName()

        x.SkipBlanks()
        let count = x.ParseNumber()

        LineCommand.Yank (lineRange, registerName, count) |> ParseResult.Succeeded

    /// Parse out the fold command
    member x.ParseFold lineRange =
        LineCommand.Fold lineRange |> ParseResult.Succeeded

    /// Parse out the join command
    member x.ParseJoin lineRange =  
        x.SkipBlanks()
        let count = x.ParseNumber()
        LineCommand.Join (lineRange, count) |> ParseResult.Succeeded

    /// Parse out the :make command.  The arguments here other than ! are undefined.  Just
    /// get the text blob and let the interpreter / host deal with it 
    member x.ParseMake () = 
        let hasBang = x.ParseBang()
        x.SkipBlanks()
        let arguments = x.ParseToEndOfLine()
        LineCommand.Make (hasBang, arguments) |> ParseResult.Succeeded

    /// Parse out the :put command.  The presence of a bang indicates that we need
    /// to put before instead of after
    member x.ParsePut lineRange =
        let hasBang = x.ParseBang()
        x.SkipBlanks()
        let registerName = x.ParseRegisterName()

        if hasBang then
            LineCommand.PutBefore (lineRange, registerName) |> ParseResult.Succeeded
        else
            LineCommand.PutAfter (lineRange, registerName) |> ParseResult.Succeeded

    /// Parse out the :retab command
    member x.ParseRetab lineRange =
        let hasBang = x.ParseBang()
        x.SkipBlanks()
        let newTabStop = x.ParseNumber()
        LineCommand.Retab (lineRange, hasBang, newTabStop) |> ParseResult.Succeeded

    /// Parse out the :set command and all of it's variants
    member x.ParseSet () = 

        // Parse out an individual option and add it to the 'withArgument' continuation
        let rec parseOption withArgument = 
            x.SkipBlanks()

            // Parse out the next argument and use 'argument' as the value of the current
            // argument
            let parseNext argument = parseOption (fun list -> argument :: list)

            // Parse out an operator.  Parse out the value and use the specified setting name
            // and argument function as the argument
            let parseOperator name argumentFunc = 
                x.IncrementIndex()
                match x.ParseWord() with
                | None -> ParseResult.Failed Resources.Parser_Error
                | Some value -> parseNext (argumentFunc (name, value))

            // Parse out a compound operator.  This is used for '+=' and such.  This will be called
            // with the index pointed at the first character
            let parseCompoundOperator name argumentFunc = 
                x.IncrementIndex()
                if x.IsCurrentCharValue '=' then
                    x.IncrementIndex()
                    parseOperator name argumentFunc
                else
                    ParseResult.Failed Resources.Parser_Error

            if x.IsAtEndOfLine then
                let list = withArgument []
                ParseResult.Succeeded (LineCommand.Set list)
            elif x.TryParseWord "all" then
                if x.IsCurrentCharValue '&' then
                    x.IncrementIndex()
                    parseNext SetArgument.ResetAllToDefault
                else
                    parseNext SetArgument.DisplayAllButTerminal
            elif x.TryParseWord "termcap" then
                parseNext SetArgument.DisplayAllTerminal
            else
                match x.ParseWord() with
                | None ->
                     ParseResult.Failed Resources.Parser_Error                   
                | Some name ->
                    if name.StartsWith("no", System.StringComparison.Ordinal) then
                        let option = name.Substring(2)
                        parseNext (SetArgument.ToggleSetting option)
                    elif name.StartsWith("inv", System.StringComparison.Ordinal) then
                        let option = name.Substring(3)
                        parseNext (SetArgument.InvertSetting option)
                    else

                        // Need to look at the next character to decide what type of 
                        // argument this is
                        match x.CurrentChar with
                        | None -> 
                            parseNext (SetArgument.DisplaySetting name)
                        | Some c ->
                            match c with 
                            | '!' -> parseNext (SetArgument.InvertSetting name)
                            | ':' -> parseOperator name SetArgument.AssignSetting
                            | '=' -> parseOperator name SetArgument.AssignSetting
                            | '+' -> parseCompoundOperator name SetArgument.AddSetting
                            | '^' -> parseCompoundOperator name SetArgument.MultiplySetting
                            | '-' -> parseCompoundOperator name SetArgument.SubtractSetting
                            | _ -> ParseResult.Failed Resources.Parser_Error

        parseOption (fun x -> x)

    /// Parse out the :source command.  It can have an optional '!' following it then a file
    /// name 
    member x.ParseSource() =
        let hasBang = x.ParseBang()
        x.SkipBlanks()
        let fileName = x.ParseToEndOfLine()
        ParseResult.Succeeded (LineCommand.Source (hasBang, fileName))

    /// Parse out the :split commnad
    member x.ParseSplit lineRange =
        x.SkipBlanks()
        let fileOptionList = x.ParseFileOptions()

        x.SkipBlanks()
        let commandOption = x.ParseCommandOption()

        ParseResult.Succeeded (LineCommand.Split (lineRange, fileOptionList, commandOption))

    /// Parse out the :qal and :quitall commands
    member x.ParseQuitAll () =
        let hasBang = x.ParseBang()
        LineCommand.QuitAll hasBang |> ParseResult.Succeeded

    /// Parse out the :quit command.
    member x.ParseQuit () = 
        let hasBang = x.ParseBang()
        LineCommand.Quit hasBang |> ParseResult.Succeeded

    /// Parse out the :display and :registers command.  Just takes a single argument 
    /// which is the register name
    member x.ParseDisplayRegisters () = 
        x.SkipBlanks()
        let name = x.ParseRegisterName()
        LineCommand.DisplayRegisters name |> ParseResult.Succeeded

    /// Parse out the :marks command.  Handles both the no argument and argument
    /// case
    member x.ParseDisplayMarks () = 
        x.SkipBlanks()

        match x.ParseWord() with
        | None ->
            // Simple case.  No marks to parse out.  Just return them all
            LineCommand.DisplayMarks List.empty |> ParseResult.Succeeded
        | Some word ->

            let mutable message : string option = None
            let list = System.Collections.Generic.List<Mark>()
            for c in word do
                match Mark.OfChar c with
                | None -> message <- Some (Resources.Parser_NoMarksMatching c)
                | Some mark -> list.Add(mark)

            match message with
            | None -> LineCommand.DisplayMarks (List.ofSeq list) |> ParseResult.Succeeded
            | Some message -> ParseResult.Failed message

    /// Parse out a single expression
    member x.ParseSingleCommand () = 

        if x.IsCurrentChar CharUtil.IsDigit then
            x.ParseJumpToLine()
        elif x.IsCurrentCharValue '$' then
            x.ParseJumpToLastLine()
        else
            let lineRange = x.ParseLineRange()

            let noRange parseFunc = 
                match lineRange with
                | None -> x.ParseClose()
                | Some _ -> ParseResult.Failed Resources.Parser_NoRangeAllowed

            // Get the command name and make sure to expand it to it's possible full
            // name
            let name = 
                if x.IsCurrentChar CharUtil.IsAlpha then
                    x.ParseWord()
                    |> OptionUtil.getOrDefault ""
                    |> x.TryExpand
                else
                    match x.CurrentChar with
                    | None -> ""
                    | Some c -> StringUtil.ofChar c

            let parseResult = 
                match name with
                | "close" -> noRange x.ParseClose
                | "cmap"-> noRange (x.ParseMapKeys false [KeyRemapMode.Command])
                | "cmapclear" -> noRange (x.ParseMapClear false [KeyRemapMode.Command])
                | "cnoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Command])
                | "cunmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.Command])
                | "delete" -> x.ParseDelete lineRange
                | "display" -> noRange x.ParseDisplayRegisters 
                | "edit" -> noRange x.ParseEdit
                | "exit" -> x.ParseQuitAndWrite lineRange
                | "fold" -> x.ParseFold lineRange
                | "iunmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.Insert])
                | "imap"-> noRange (x.ParseMapKeys false [KeyRemapMode.Insert])
                | "imapclear" -> noRange (x.ParseMapClear false [KeyRemapMode.Insert])
                | "inoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Insert])
                | "join" -> x.ParseJoin lineRange 
                | "lmap"-> noRange (x.ParseMapKeys false [KeyRemapMode.Language])
                | "lunmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.Language])
                | "lnoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Language])
                | "make" -> noRange x.ParseMake 
                | "marks" -> noRange x.ParseDisplayMarks
                | "map"-> noRange (x.ParseMapKeys true [KeyRemapMode.Normal;KeyRemapMode.Visual; KeyRemapMode.Select;KeyRemapMode.OperatorPending])
                | "mapclear" -> noRange (x.ParseMapClear true [KeyRemapMode.Normal; KeyRemapMode.Visual; KeyRemapMode.Command; KeyRemapMode.OperatorPending])
                | "nmap"-> noRange (x.ParseMapKeys false [KeyRemapMode.Normal])
                | "nmapclear" -> noRange (x.ParseMapClear false [KeyRemapMode.Normal])
                | "nnoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Normal])
                | "nunmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.Normal])
                | "nohlsearch" -> noRange (fun () -> LineCommand.NoHlSearch |> ParseResult.Succeeded)
                | "noremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Normal;KeyRemapMode.Visual; KeyRemapMode.Select;KeyRemapMode.OperatorPending])
                | "omap"-> noRange (x.ParseMapKeys false [KeyRemapMode.OperatorPending])
                | "omapclear" -> noRange (x.ParseMapClear false [KeyRemapMode.OperatorPending])
                | "onoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.OperatorPending])
                | "ounmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.OperatorPending])
                | "put" -> x.ParsePut lineRange
                | "quit" -> noRange x.ParseQuit
                | "qall" -> noRange x.ParseQuitAll
                | "quitall" -> noRange x.ParseQuitAll
                | "redo" -> noRange (fun () -> LineCommand.Redo |> ParseResult.Succeeded)
                | "retab" -> x.ParseRetab lineRange
                | "set" -> noRange x.ParseSet
                | "source" -> noRange x.ParseSource
                | "split" -> x.ParseSplit lineRange
                | "registers" -> noRange x.ParseDisplayRegisters 
                | "substitute" -> x.ParseSubstitute lineRange (fun x -> x)
                | "smagic" -> x.ParseSubstituteMagic lineRange
                | "smap"-> noRange (x.ParseMapKeys false [KeyRemapMode.Select])
                | "smapclear" -> noRange (x.ParseMapClear false [KeyRemapMode.Select])
                | "snomagic" -> x.ParseSubstituteNoMagic lineRange
                | "snoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Select])
                | "sunmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.Select])
                | "tabfirst" -> noRange (fun () -> ParseResult.Succeeded LineCommand.GotoFirstTab)
                | "tabrewind" -> noRange (fun () -> ParseResult.Succeeded LineCommand.GotoFirstTab)
                | "tablast" -> noRange (fun () -> ParseResult.Succeeded LineCommand.GotoLastTab)
                | "tabnext" -> noRange x.ParseTabNext 
                | "tabNext" -> noRange x.ParseTabPrevious
                | "tabprevious" -> noRange x.ParseTabPrevious
                | "undo" -> noRange (fun () -> LineCommand.Undo |> ParseResult.Succeeded)
                | "unmap" -> noRange (x.ParseMapUnmap true [KeyRemapMode.Normal;KeyRemapMode.Visual; KeyRemapMode.Select;KeyRemapMode.OperatorPending])
                | "vmap"-> noRange (x.ParseMapKeys false [KeyRemapMode.Visual;KeyRemapMode.Select])
                | "vmapclear" -> noRange (x.ParseMapClear false [KeyRemapMode.Visual; KeyRemapMode.Select])
                | "vnoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Visual;KeyRemapMode.Select])
                | "vunmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.Visual;KeyRemapMode.Select])
                | "wq" -> x.ParseQuitAndWrite lineRange
                | "xit" -> x.ParseQuitAndWrite lineRange
                | "xmap"-> noRange (x.ParseMapKeys false [KeyRemapMode.Visual])
                | "xmapclear" -> noRange (x.ParseMapClear false [KeyRemapMode.Visual])
                | "xnoremap"-> noRange (x.ParseMapKeysNoRemap false [KeyRemapMode.Visual])
                | "xunmap" -> noRange (x.ParseMapUnmap false [KeyRemapMode.Visual])
                | "yank" -> x.ParseYank lineRange
                | "/" -> noRange (x.ParseSearch Path.Forward)
                | "?" -> noRange (x.ParseSearch Path.Backward)
                | "<" -> x.ParseShiftLeft lineRange
                | ">" -> x.ParseShiftRight lineRange
                | "&" -> x.ParseSubstituteRepeatLast lineRange
                | "~" -> x.ParseSubstituteRepeatLastWithSearch lineRange
                | _ -> ParseResult.Failed Resources.Parser_Error

            match parseResult with
            | ParseResult.Failed _ ->
                // If there is already a failure don't look any deeper.
                parseResult
            | ParseResult.Succeeded _ ->
                x.SkipBlanks()

                // If there are still characters then it's illegal trailing characters
                if Option.isSome x.CurrentChar then
                    ParseResult.Failed Resources.CommandMode_TrailingCharacters
                else
                    parseResult

    // TODO: Delete.  This is just a transition hack to allow us to use the new interpreter and parser
    // to replace RangeUtil.ParseRange
    static member ParseRange rangeText = 
        let parser = Parser(rangeText)
        let lineRange = parser.ParseLineRange()
        match lineRange with 
        | None -> ParseResult.Failed Resources.Parser_Error
        | Some lineRange -> ParseResult.Succeeded (lineRange, parser.RemainingText) 

    static member ParseExpression (expressionText : string) : ParseResult<Expression> = 
        ParseResult.Failed Resources.Parser_Error

    static member ParseLineCommand (commandText : string) = 
        let parser = Parser(commandText)
        parser.ParseSingleCommand()
