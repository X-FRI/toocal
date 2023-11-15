module Toocal.Core.DataAccessLayer.Dal

open System
open ZeroLog
open Toocal.Core.Function.Retry
open Toocal.Core.Function.Ignore
open Toocal.Core.Errors
open Toocal.Core.Errors.DataAccessLayer
open Toocal.Core.DataAccessLayer.Page
open Toocal.Core.DataAccessLayer.Freelist
open Toocal.Core.DataAccessLayer.Meta

/// Data Access Layer (DAL) handles all disk operations and how data is
/// organized on the disk. It’s responsible for managing the underlying data
/// structure, writing the database pages to the disk, and reclaiming free pages
/// to avoid fragmentation.
type Dal (path : String, pageSize : int32) =
  static let logger = LogManager.GetLogger("Toocal.Core.DataAccessLayer.Dal")
  let file = Dal.InitFile(path) => (fun e -> logger.Error(e.ToString()))

  interface IDisposable with
    member this.Dispose () = !(fun () -> file.Dispose())

  member public this.Freelist = new Freelist()
  member public this.Meta = new Meta()

  static member public InitFile (path : String) : Dal.Result<IO.FileStream> =
    try
      IO.File.Open(path, IO.FileMode.OpenOrCreate, IO.FileAccess.ReadWrite)
      |> Ok
    with e ->
      Dal.CannotOpenFile path |> Error

  member public this.AllocateEmptyPage (num : PageNum) =
    new Page(num, Array.zeroCreate<Byte> (pageSize))

  member public this.ReadPage (num : PageNum) =
    let data = Array.zeroCreate<Byte> (pageSize)
    let offset = ((num |> int32) * pageSize) |> int64

    !(fun _ -> file.Seek(offset, IO.SeekOrigin.Begin))
    == !(fun _ -> file.Read(data))

    new Page(num, data)

  member public this.WritePage (page : Page) =
    let offset = ((page.Num |> int32) * pageSize) |> int64

    !(fun _ -> file.Seek(offset, IO.SeekOrigin.Begin))
    == !(fun _ -> file.Write(page.Data))

  member public this.WriteMeta (meta : Meta) =
    let page = new Page(Meta.META_PAGE_NUM, Array.zeroCreate<Byte> (pageSize))

    meta.Serialize(page.Data)
    this.WritePage(page)

    page

  member public this.ReadMeta () =
    let page = this.ReadPage(Meta.META_PAGE_NUM)
    let meta = new Meta()

    meta.Deserialize(page.Data)
    page