
namespace Tonyx.EventSourcing.Sample
open Tonyx.EventSourcing
open Tonyx.EventSourcing.Utils
open Tonyx.EventSourcing.Repository

open Tonyx.EventSourcing.Sample.TodosAggregate
open Tonyx.EventSourcing.Sample.Todos.TodoEvents
open Tonyx.EventSourcing.Sample.Todos.TodoCommands
open Tonyx.EventSourcing.Sample.Todos.Models.TodosModel
open Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel

open Tonyx.EventSourcing.Sample.TagsAggregate
open Tonyx.EventSourcing.Sample.Tags.TagsEvents
open Tonyx.EventSourcing.Sample.Tags.TagCommands
open Tonyx.EventSourcing.Sample.Tags.Models.TagsModel

open Tonyx.EventSourcing.Sample_02
open Tonyx.EventSourcing.Sample_02.Categories
open Tonyx.EventSourcing.Sample_02.CategoriesAggregate
open Tonyx.EventSourcing.Sample_02.Categories.CategoriesCommands
open Tonyx.EventSourcing.Sample_02.Categories.CategoriesEvents
open System
open FSharpPlus

module AppCoordinator =

    type Application =
        {
            getAllTodos:        unit -> Result<List<Todo>, string>
            addTodo:            Todo -> Result<unit, string>
            add2Todos:          Todo * Todo -> Result<unit, string>
            removeTodo:         Guid -> Result<unit, string>

            getAllCategories:   unit -> Result<List<Category>, string> 
            addCategory:        Category -> Result<unit, string>
            removeCategory:     Guid -> Result<unit, string>
            addTag:             Tag -> Result<unit, string>
            removeTag:          Guid -> Result<unit, string>
            getAllTags:         unit -> Result<List<Tag>, string>
        }

    let applicationVersion1 =
        {
            getAllTodos =       App.getAllTodos
            addTodo =           App.addTodo
            add2Todos =         App.add2Todos
            removeTodo =        App.removeTodo
            getAllCategories =  App.getAllCategories
            addCategory =       App.addCategory
            removeCategory =    App.removeCategory 
            addTag =            App.addTag 
            removeTag =         App.removeTag
            getAllTags =        App.getAllTags  
        }

    let applicationVersion2 =
        {
            getAllTodos =       App.getAllTodos
            addTodo =           App.addTodo'
            add2Todos =         App.add2Todos'
            removeTodo =        App.removeTodo
            getAllCategories =  App.getAllCategories'
            addCategory =       App.addCategory'
            removeCategory =    App.removeCategory'
            addTag =            App.addTag 
            removeTag =         App.removeTag
            getAllTags =        App.getAllTags
        }

    ()