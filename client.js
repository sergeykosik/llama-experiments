import axios from 'axios';

const callApi = async (message) => {
  try {
    const response = await axios.post('http://localhost:3000/process-message', {
      message: message,
    }, {
      headers: {
        'Content-Type': 'application/json',
      },
    });
    console.log('Response from API:', response.data);
  } catch (error) {
    console.error('Error calling API:', error.response ? error.response.data : error.message);
  }
};

// Replace with the message you want to send
const message = `Create a JSON that contains a message as a result for each: Convert the following c# lines to use interpolated string: 
  Line 56: system = cl.Name + "Class " + cl.Name + " is used in trial balance " + o.Id + " some");
  Line 61: messages.Add("Class " + cl.Name + " is used in trial balance " + o.Id);
  Line 66: messages.Add("Class " + cl.Name + " is used in trial balance");
  Line 72: messages.Add("Class " + cl.Name + " is variant");
  Line 78: throw new ApplicationException("Unknown verb: " + verb);
  `;

const message2 = `Transform the following concatenated string lines into interpolated strings. Return the result as a JSON object where each key is the line number and the value is the transformed string. For example: { "Line1": "Transformed string" }\n\n` + 
  `Line 56: system = cl.Name + "Class " + cl.Name + " is used in trial balance " + o.Id + " some");
  Line 61: messages.Add("Class " + cl.Name + " is used in trial balance " + o.Id);
  Line 66: messages.Add("Class " + cl.Name + " is used in trial balance");
  Line 72: messages.Add("Class " + cl.Name + " is variant");
  Line 78: throw new ApplicationException("Unknown verb: " + verb);`;


const prompt = `Create a JSON that contains a message as a result: Given c# snippet, find concatenated strings, such as "Some text" + o.Type + "other text", and replace them with interpolated strings:
{fileContent}
`;
callApi(message2);
