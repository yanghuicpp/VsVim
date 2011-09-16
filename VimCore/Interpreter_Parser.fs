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
    ]

    /// Current index into the expression text
    let mutable _index = 0

    member x.CurrentChar =
        if _index >= _text.Length then
            None
        else
            Some _text.[_index]

    member x.IsCurrentChar predicate = 
        match x.CurrentChar with
        | None -> false
        | Some c -> predicate c

    member x.IsCurrentCharValue value =
        match x.CurrentChar with
        | None -> false
        | Some c -> c = value

    /// Does the text begin with the provided value
    member x.IsCurrentString value = 
        _text.Substring(_index).StartsWith(value)

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
        let fileName =
            match x.CurrentChar with
            | None -> None
            | Some _ -> x.ParseToEndOfLine() |> Some

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
    member x.ParseSubstitute lineRange = 
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
                    x.SkipBlanks()
                    let count = x.ParseNumber()
                    let command = LineCommand.Substitute (lineRange, pattern, replace, flags, count)
                    ParseResult.Succeeded command
        else
            ParseResult.Failed Resources.Parser_Error

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

            // Get the command name and make sure to expand it to ti's possible full
            // name
            let name = 
                x.ParseWord()
                |> OptionUtil.getOrDefault ""
                |> x.TryExpand

            let parseResult = 
                match name with
                | "close" -> noRange x.ParseClose
                | "delete" -> x.ParseDelete lineRange
                | "display" -> noRange x.ParseDisplayRegisters 
                | "edit" -> noRange x.ParseEdit
                | "exit" -> x.ParseQuitAndWrite lineRange
                | "fold" -> x.ParseFold lineRange
                | "join" -> x.ParseJoin lineRange 
                | "make" -> noRange x.ParseMake 
                | "marks" -> noRange x.ParseDisplayMarks
                | "registers" -> noRange x.ParseDisplayRegisters 
                | "substitute" -> x.ParseSubstitute lineRange
                | "wq" -> x.ParseQuitAndWrite lineRange
                | "xit" -> x.ParseQuitAndWrite lineRange
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

