// function newAlert (message) {
//     alert(message);
// }
// module.exports = {
//     // parseJson : string -> obj option
//     parseJson: function (jsonContent) {
//         try {
//             var result = JSON.parse(jsonContent);
//             return result;
//         } catch(ex) {
//             return null;
//         }
//     },
//     newAlert: function (message) {
//         alert(message);
//     },
//
//     // getValue : obj -> string -> obj option
//     getValue: function(obj, prop) {
//         var result = obj[prop];
//         if (result === undefined) {
//             return null;
//         } else {
//             return result;
//         }
//     }
// };