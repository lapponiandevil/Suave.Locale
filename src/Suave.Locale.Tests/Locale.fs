module Suave.Locale.Tests.Locale

open System
open Suave
open Suave.Web
open Suave.Http
open Suave.Types
open Suave.Testing
open Arachne.Http
open Arachne.Language
open Chiron
open Suave.Locale
open Fuchu

[<Tests>]
let range =
  testList "Range" [
    testList "generalise" [
      testCase "en-GB" <| fun _ ->
        Assert.Equal("next up", Range ["en"], Range.generalise (Range ["en"; "GB"]))

      testCase "en" <| fun _ ->
        Assert.Equal("next up", Range ["en"], Range.generalise (Range ["en"]))

      testCase "en-GB-XPrivate" <| fun _ ->
        Assert.Equal("next up", Range ["en"; "GB"], Range.generalise (Range ["en"; "GB"; "XPrivate"]))
      ]
    testList "checkParent" [
      testCase "en, en => Range [ en ]" <| fun _ ->
        Assert.Equal("", Choice1Of2 (Range ["en"]), Range.checkParent (Range ["en"]) (Range ["en"]))

      testCase "en, en-GB => Choice1Of2 $ Range [ en ]" <| fun _ ->
        Assert.Equal("", Choice1Of2 (Range ["en"]), Range.checkParent (Range ["en"]) (Range ["en"; "GB"]))

      testCase "en-GB, en => Choice2Of2 ()" <| fun _ ->
        Assert.Equal("", Choice2Of2 (), Range.checkParent (Range ["en"; "GB"]) (Range ["en"]))

      testCase "en, Any => Range [ en ]" <| fun _ ->
        Assert.Equal("", Choice1Of2 (Range ["en"]), Range.checkParent (Range ["en"]) (Range ["en"]))
      ]
    ]

let emptyData r =
  IntlData.Create(
    [r],
    """{"misc":{"title":"Häftig titel"}}""" |> Json.parse |> Json.deserialize)

let wonkyMsgs = """{"locales":["en"],"messages":{"misc":{"title":"Awesome Title"}}}"""

[<Tests>]
let intlData =
  testList "intl data" [
    testCase "from json" <| fun _ ->
      let intl : IntlData = Json.parse wonkyMsgs |> Json.deserialize
      Assert.Equal("locale", [ Range ["en"] ], intl.locales)
      Assert.Equal("title", "Awesome Title", intl |> IntlData.find ["misc";"title"])

    testCase "multi-length key" <| fun _ ->
      let data = {locales = []; messages = Messages [(["en"; "GB"], "Hello World")];}
      //printfn "json: %A" (data |> Json.serialize)
      Assert.Equal("", data, data |> Json.serialize |> Json.deserialize)

    testCase "back and forth with single key-message" <| fun _ ->
      let data = {locales = []; messages = Messages [(["L^"], "a")];}
      Assert.Equal("", data, data |> Json.serialize |> Json.deserialize)

    testCase "back and forth with >1 message" <| fun _ ->
      let data = { locales  = []
                   messages =
                     Messages [
                       (["o"; "\"n3UK"; "<2MC"; "5+MC"; "9/"; "/"; "F<^*o"; "@6XNt"; "7|r"; "0&";
                         "~"; "(m"; "3^lzXu."; "2("], "a")
                       (["o"; "/tj"; "$O]kI"; "9/Q"; "VbD2!"; "\(m"; "@6X"; "OH|L"; "<gu"; "l.D[D";
                         "4"; "x=3U!f\~J"; "X"; "S"; "i."; "5"; "&H"], "a")
                   ] }
      //printfn "json: %A" (data |> Json.serialize)
      Assert.Equal("", data, data |> Json.serialize |> Json.deserialize)
                  
    testPropertyWithConfig fsCheckConfig "back and forth" <| fun (intl : IntlData) ->
      Tests.skiptest ("TODO: ensure that FsCheck doesn't generate two translation keys k1, k2 such that" +
                      "k1 is a leaf node and k1 is also a prefix of k2 as this will make input" +
                      "not equal to output and fail the property")

      // TODO: ensure that FsCheck doesn't generate identical translation keys k1, k2
      let subject : IntlData = intl |> Json.serialize |> Json.deserialize
      let (Messages msgs) = intl.messages
      for (k, tr) in msgs do
        let (Messages trMsgs) = subject.messages
        try
          Assert.Equal("eq", tr, trMsgs |> List.find (fun (key, _) -> key = k) |> snd)
        with e ->
          let b : string -> byte [] = System.Text.Encoding.UTF8.GetBytes
          //printfn "key: %A, subject: %A, expected: %A" (List.map b k) subject intl
          reraise ()

    testCase "merge" <| fun _ ->
      Tests.skiptest "To be done – help requested"

      let svSE =
        [ [ "frontpage"; "menu"; "home" ], "Hem" // both have, this overwritten
          [ "frontpage"; "menu"; "logout" ], "Logga Ut" // fi doesn't have
          [ "settings"; "username" ], "haf" // fi doesn't have, new parent key-space
          [ "frontpage"; "menu"; "submenu" ], "TODO" // fi has extra level
        ]

      let svFI =
        [ [ "frontpage"; "menu"; "home" ], "Hemm" // both have, this chosedn
          [ "frontpage"; "menu"; "help" ], "Hjälp" // sv doesn't have
          [ "frontpage"; "menu"; "submenu"; "details" ], "Detaljer" // sv missing level, this chosen
        ]
      let expMerged =
        [ [ "frontpage"; "menu"; "home" ], "Hemm"
          [ "frontpage"; "menu"; "logout" ], "Logga Ut"
          [ "frontpage"; "menu"; "help" ], "Hjälp"
          [ "settings"; "username" ], "haf"
          [ "frontpage"; "menu"; "submenu"; "details" ], "Detaljer"
        ]
      let sv = IntlData.Create(Range ["sv"; "SE"], svSE)
      let fi = IntlData.Create(Range ["sv"; "FI"], svFI)

      let merged = IntlData.merge sv fi
      Assert.Equal("merges locales", [ Range [ "sv"; "SE" ]; Range [ "sv"; "FI" ] ], merged.locales)
      let (Messages subject) = merged.messages

      for (k, tr) in expMerged do
        Assert.Equal(sprintf "merged %A" k, tr, merged |> IntlData.find k)
    ]

[<Tests>]
let negotiate =
  let wants matching = function
    | Range rs as r when rs = matching ->
      //printfn "testing %A, matching %A" rs matching
      Choice1Of2 (emptyData r)
    | Any                              ->
      Choice1Of2 (emptyData (Range ["en"]))
    | rs                                ->
      //printfn "testing %A, not matching %A" rs matching
      Choice2Of2 ()
  let en = wants [ "en" ]
  let enGB = wants [ "en"; "GB" ]

  testList "Negotiate" [
    testList "findParent" [
      testCase "(Source 'en') Any => Choice1Of2 'en'" <| fun _ ->
        Assert.Equal("", Choice1Of2 (emptyData (Range ["en"])), Negotiate.findParent en Any)

      testCase "(Source 'en-GB') 'en' => Choice2Of2 ()" <| fun _ ->
        Assert.Equal("", Choice2Of2 (), Negotiate.findParent enGB (Range ["en"]))

      testCase "(Source 'en-GB') 'en-GB' => Choice2Of2 ()" <| fun _ ->
        Assert.Equal("",
                     Choice1Of2 (emptyData (Range ["en"; "GB"])),
                     Negotiate.findParent enGB (Range ["en"; "GB"]))

      testCase "(Source 'en') 'en-GB' => Choice1Of2 'en'" <| fun _ ->
        Assert.Equal("",
                     Choice1Of2 (emptyData (Range ["en"])),
                     Negotiate.findParent en (Range ["en"; "GB"]))
    ]
  ]

open HttpFs

[<Tests>]
let http =
  let neg =
    Negotiate.negotiate
      [ ReqSources.parseAcceptable
        ReqSources.always (Range [ "sv"; "FI" ])
      ]
      [ LangSources.fromJson wonkyMsgs
        LangSources.always (emptyData (Range ["sv"; "FI"])) ]
    |> Negotiate.assumeSource

  let clientNegf fHeader =
    let ctx = runWith defaultConfig (Http.api "/intl" neg)

    try
      use resp =
        Client.createRequest Client.Get (Uri "http://127.0.0.1:8083/intl")
        |> fHeader
        |> Client.getResponse
        |> Async.RunSynchronously
      
      let data : IntlData =
        resp
        |> Client.Response.readBodyAsString
        |> Async.RunSynchronously
        |> Json.parse
        |> Json.deserialize

      data

    finally
      disposeContext ctx

  let clientNeg header = clientNegf (Client.withHeader (Client.RequestHeader.AcceptLanguage header))

  testList "Http" [
    testCase "negotiate" <| fun _ ->
      let data = clientNeg "ro, en, sv"
      Assert.Equal("locales", [ Range [ "sv"; "FI" ] ], data.locales)

    testCase "negotiate non existing, chooses default" <| fun _ ->
      let data = clientNeg "ro"
      Assert.Equal("locales", [ Range [ "sv"; "FI" ] ], data.locales)

    testCase "negotiate w/ mute client, choose default" <| fun _ ->
      let data = clientNegf id
      Assert.Equal("locales", [ Range [ "sv"; "FI" ] ], data.locales)
    ]